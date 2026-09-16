using System.Text.Json;
using Stats.Core.Metrics;
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
