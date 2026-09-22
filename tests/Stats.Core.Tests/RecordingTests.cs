using System.Text.Json;
using Stats.Core.Metrics;
using Stats.Core.Frames;
using Stats.Core.Recording;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public sealed class RecordingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.RecordingTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Cpu = new("cpu", "CPU", MetricGroup.Cpu, "CPU", "°C", "F1");

    [Fact]
    public async Task Recorder_RoundTripsMissingAndFrozenSelection_StopIsIdempotent_UsesUniquePaths()
    {
        using var recorder = new SessionRecorder(_directory);
        var definitions = new[] { Cpu };
        recorder.Start(definitions, Start);
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 999 }, Start.AddSeconds(-1))); // queued before Start
        definitions[0] = Cpu with { Id = "other" };
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 62 }, Start));
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = float.NaN }, Start.AddSeconds(2)));
        await recorder.StopAsync();
        await recorder.StopAsync();
        Assert.Null(recorder.Error);
        var first = recorder.FilePath!;
        var data = SessionFile.Load(first);
        Assert.True(data.IsComplete);
        Assert.Equal(2, data.SampleCount);
        Assert.Equal(new float?[] { 62, null }, Assert.Single(data.Series).Values);
        Assert.Equal(new[] { Start, Start.AddSeconds(2) }, data.Series[0].TimesUtc);
        Assert.Equal(62f, Assert.Single(data.Summaries).Average);
        recorder.Start(new[] { Cpu }, Start);
        await recorder.StopAsync();
        Assert.NotEqual(first, recorder.FilePath);
        Assert.True(File.Exists(first));
        Assert.Equal(2, SessionFile.Load(first).SampleCount);
    }

    [Fact]
    public void Recorder_HeaderFailureNeverArms()
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, "not-a-directory");
        File.WriteAllText(file, "preserved");
        using var recorder = new SessionRecorder(file);
        Assert.ThrowsAny<IOException>(() => recorder.Start(new[] { Cpu }, Start));
        Assert.False(recorder.IsRecording);
        Assert.NotNull(recorder.Error);
        Assert.Equal("preserved", File.ReadAllText(file));
    }

    [Fact]
    public async Task Recorder_BackwardsClockStopsAndLeavesReadablePartialSession()
    {
        using var recorder = new SessionRecorder(_directory);
        recorder.Start(new[] { Cpu }, Start);
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 62 }, Start));
        recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = 63 }, Start.AddSeconds(-1)));
        await recorder.StopAsync();
        Assert.False(recorder.IsRecording);
        Assert.NotNull(recorder.Error);
        var data = SessionFile.Load(recorder.FilePath!);
        Assert.False(data.IsComplete);
        Assert.Equal(1, data.SampleCount);
    }

    [Fact]
    public void ReaderBoundsOnlyCharts_ExportKeepsEverySampleAndProtectsHeaders()
    {
        var file = CreateRecording(SessionFile.ChartSampleLimit + 2, Cpu with { DisplayName = "=CPU,\"quoted\"" });
        var data = SessionFile.Load(file);
        Assert.Equal(SessionFile.ChartSampleLimit + 2, data.SampleCount);
        Assert.Equal(SessionFile.ChartSampleLimit, data.Series[0].Values.Length);
        Assert.Equal(Start.AddSeconds(2), data.Series[0].TimesUtc[0]);
        Assert.Equal(0f, data.Summaries[0].Min);
        Assert.Equal(3601f, data.Summaries[0].Max);
        var csv = Path.Combine(_directory, "export.csv");
        SessionFile.ExportCsv(file, csv);
        var lines = File.ReadAllLines(csv);
        Assert.Equal(SessionFile.ChartSampleLimit + 3, lines.Length);
        Assert.StartsWith("\"UTC\",\"'=CPU,\"\"quoted\"\"", lines[0]);
        Assert.EndsWith(",0", lines[1]);
        Assert.EndsWith(",3601", lines[^1]);
    }

    [Fact]
    public void InterruptedTailLoadsButMalformedInteriorFailsWithoutReplacingCsv()
    {
        var file = CreateRecording(2, Cpu, complete: false);
        File.AppendAllText(file, "{\"type\":\"sample\"");
        var partial = SessionFile.Load(file);
        Assert.False(partial.IsComplete);
        Assert.Equal(2, partial.SampleCount);
        File.AppendAllText(file, "\n{}\n");
        var csv = Path.Combine(_directory, "old.csv");
        File.WriteAllText(csv, "old export");
        Assert.ThrowsAny<JsonException>(() => SessionFile.ExportCsv(file, csv));
        Assert.Equal("old export", File.ReadAllText(csv));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public void InvalidImportedFormatAndExportOverRecordingAreRejected()
    {
        var invalid = CreateRecording(1, Cpu with { Format = "F999999999" });
        Assert.Throws<InvalidDataException>(() => SessionFile.Load(invalid));
        var valid = CreateRecording(1, Cpu);
        var original = File.ReadAllText(valid);
        Assert.Throws<IOException>(() => SessionFile.ExportCsv(valid, valid));
        Assert.Equal(original, File.ReadAllText(valid));
    }

    [Fact]
    public async Task Recorder_RawFramesAreOptIn_AndUseExactPercentilesAndHitchBoundary()
    {
        using var recorder = new SessionRecorder(_directory);
        recorder.Start([Cpu], Start, includeFrames: false);
        recorder.RecordFrame(new(Start, 42, 100));
        await recorder.StopAsync();
        Assert.Null(SessionFile.Load(recorder.FilePath!).FrameSummary);

        recorder.Start([Cpu], Start, includeFrames: true, gameName: "Game");
        foreach (var milliseconds in new[] { 10d, 20, 30, 40, 50, 50.1, 100 })
            recorder.RecordFrame(new(Start, 42, milliseconds));
        await recorder.StopAsync();

        var data = SessionFile.Load(recorder.FilePath!);
        var summary = Assert.IsType<SessionFrameSummary>(data.FrameSummary);
        Assert.Equal("Game", data.GameName);
        Assert.Equal(7, summary.Count);
        Assert.Equal(40, summary.P50FrameTimeMs);
        Assert.Equal(100, summary.P95FrameTimeMs);
        Assert.Equal(100, summary.P99FrameTimeMs);
        Assert.Equal(2, summary.HitchFrameCount); // strictly > 50 ms
    }

    [Fact]
    public async Task Recorder_V2AllowsConcurrentFrameAndPollStreams()
    {
        using var recorder = new SessionRecorder(_directory);
        recorder.Start([Cpu], Start, includeFrames: true);
        var frames = Task.Run(() =>
        {
            for (var i = 0; i < 20; i++) recorder.RecordFrame(new(Start.AddMilliseconds(i), 42, 16.6));
        });
        for (var i = 0; i < 20; i++)
            recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = i }, Start.AddSeconds(i)));
        await frames;
        await recorder.StopAsync();

        var data = SessionFile.Load(recorder.FilePath!);
        Assert.Null(recorder.Error);
        Assert.Equal(20, data.SampleCount);
        Assert.Equal(20, Assert.IsType<SessionFrameSummary>(data.FrameSummary).Count);
    }

    [Fact]
    public async Task Recorder_RawFrameBackpressureEndsVisiblyInsteadOfDropping()
    {
        using var sink = new StalledWriter();
        using var recorder = new SessionRecorder(_directory, openWriter: _ => sink);
        recorder.Start([Cpu], Start, includeFrames: true);
        try
        {
            recorder.RecordFrame(new(Start, 42, 16.6));
            await sink.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            for (var i = 0; i < 8193 && recorder.IsRecording; i++)
                recorder.RecordFrame(new(Start.AddMilliseconds(i), 42, 16.6));
        }
        finally { sink.Release.TrySetResult(); await recorder.StopAsync(); }
        Assert.NotNull(recorder.Error);
        Assert.Contains("could not keep up", recorder.Error, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StalledWriter : StringWriter
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task WriteLineAsync(string? value)
        {
            Entered.TrySetResult();
            await Release.Task;
            await base.WriteLineAsync(value);
        }
    }

    [Fact]
    public async Task OldFrameSinkCannotWriteIntoOrFailTheNextSession_AndFramesExtendEndTime()
    {
        using var recorder = new SessionRecorder(_directory, () => Start);
        recorder.Start([Cpu], Start, includeFrames: true);
        var stale = recorder.CreateFrameSink();
        await recorder.StopAsync();
        recorder.Start([Cpu], Start.AddSeconds(1), includeFrames: true);
        stale(new(Start.AddSeconds(5), 42, 999));
        stale(new(Start, -1, double.NaN));
        recorder.CreateFrameSink()(new(Start.AddSeconds(3), 42, 16));
        await recorder.StopAsync();
        var data = SessionFile.Load(recorder.FilePath!);
        Assert.Null(recorder.Error);
        Assert.Equal(1, data.FrameSummary!.Count);
        Assert.Equal(16, data.FrameSummary.MaxFrameTimeMs);
        Assert.Equal(Start.AddSeconds(3), data.EndedUtc);
    }

    [Theory]
    [InlineData(-1, 2)]
    [InlineData(2, 1)]
    public void RawFramesMustStayWithinSessionBounds(int frameSeconds, int endSeconds)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "bounds.stats-session.jsonl");
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new { type = "header", version = 2, startedUtc = Start, metrics = new[] { Cpu } }),
            JsonSerializer.Serialize(new { type = "frame", timestampUtc = Start.AddSeconds(frameSeconds), pid = 42, frameTimeMs = 16 }),
            JsonSerializer.Serialize(new { type = "end", endedUtc = Start.AddSeconds(endSeconds) }),
        ]);
        Assert.Throws<InvalidDataException>(() => SessionFile.Load(path));
    }

    [Fact]
    public void IncompleteRawOnlyReportKeepsObservedDuration()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "partial-raw.stats-session.jsonl");
        File.WriteAllLines(path,
        [
            JsonSerializer.Serialize(new { type = "header", version = 2, startedUtc = Start, metrics = new[] { Cpu }, includeFrames = true }),
            JsonSerializer.Serialize(new { type = "frame", timestampUtc = Start.AddSeconds(30), pid = 42, frameTimeMs = 16 }),
        ]);
        var data = SessionFile.Load(path);
        Assert.False(data.IsComplete);
        Assert.Equal(TimeSpan.FromSeconds(30), SessionReports.Build(data).Duration);
    }

    [Fact]
    public void Load_VersionOneRemainsPollOnly_AndVersionTwoAllowsInterleavedFrames()
    {
        var v1 = CreateRecording(1, Cpu);
        Assert.Null(SessionFile.Load(v1).FrameSummary);

        var v2 = Path.Combine(_directory, "v2.stats-session.jsonl");
        File.WriteAllLines(v2,
        [
            JsonSerializer.Serialize(new { type = "header", version = 2, startedUtc = Start, metrics = new[] { Cpu }, gameName = "Game" }),
            JsonSerializer.Serialize(new { type = "frame", timestampUtc = Start.AddSeconds(2), pid = 10, frameTimeMs = 20d }),
            JsonSerializer.Serialize(new { type = "sample", timestampUtc = Start.AddSeconds(1), values = new float?[] { 1 } }),
            JsonSerializer.Serialize(new { type = "end", endedUtc = Start.AddSeconds(3) }),
        ]);
        var data = SessionFile.Load(v2);
        Assert.Equal("Game", data.GameName);
        Assert.Equal(1, data.SampleCount);
        Assert.Equal(20, Assert.IsType<SessionFrameSummary>(data.FrameSummary).P99FrameTimeMs);
    }

    [Fact]
    public void HistoryTimestampRing_TracksWrapResizeGapsAndReset()
    {
        var history = new MetricHistory(3);
        history.Add(1, Start);
        history.Add(null, Start.AddSeconds(1));
        history.Add(3, Start.AddSeconds(3));
        history.Add(4, Start.AddSeconds(4));
        Assert.Equal(new float?[] { null, 3, 4 }, history.CopySeries(Cpu).Values);
        history.Resize(2);
        var copied = history.CopySeries(Cpu);
        Assert.Equal(new float?[] { 3, 4 }, copied.Values);
        Assert.Equal(new[] { Start.AddSeconds(3), Start.AddSeconds(4) }, copied.TimesUtc);
        history.Resize(5);
        Assert.Equal(copied.TimesUtc, history.CopySeries(Cpu).TimesUtc);
        history.ResetSession();
        Assert.Empty(history.CopySeries(Cpu).TimesUtc);
        history.Add(7, Start.AddSeconds(7));
        Assert.Equal(new[] { Start.AddSeconds(7) }, history.CopySeries(Cpu).TimesUtc);
    }

    private string CreateRecording(int count, MetricDefinition definition, bool complete = true)
    {
        Directory.CreateDirectory(_directory);
        var file = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".stats-session.jsonl");
        using var writer = File.CreateText(file);
        writer.WriteLine(JsonSerializer.Serialize(new { type = "header", version = 1, startedUtc = Start, metrics = new[] { definition } }));
        for (var i = 0; i < count; i++)
            writer.WriteLine(JsonSerializer.Serialize(new { type = "sample", timestampUtc = Start.AddSeconds(i), values = new float?[] { i } }));
        if (complete) writer.WriteLine(JsonSerializer.Serialize(new { type = "end", endedUtc = Start.AddSeconds(count) }));
        return file;
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }
}
