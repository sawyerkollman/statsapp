using Stats.Core.Metrics;
using Stats.Core.Recording;

namespace Stats.Core.Tests;

public sealed class SessionLabTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.SessionLab", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Cpu = new("cpu", "CPU", MetricGroup.Cpu, "CPU", "%");

    [Fact]
    public void LoadWindow_ClampsToRequestedRange_AndPreservesMissing()
    {
        var path = Write([1f, null, 3f, 4f]);
        var data = SessionFile.LoadWindow(path, 1, 2);
        Assert.Equal(4, data.SampleCount);
        Assert.Equal(new float?[] { null, 3f }, data.Series[0].Values);
        Assert.Equal(Start.AddSeconds(1), data.Series[0].TimesUtc[0]);
        Assert.Empty(SessionFile.LoadWindow(path, 99, 2).Series[0].Values);
    }

    [Fact]
    public void Annotations_RoundTripAndInvalidFileIsSurfaced()
    {
        var path = Write([1f]);
        SessionAnnotationStore.Save(path, [new(2, "note")]);
        Assert.Equal("note", Assert.Single(SessionAnnotationStore.Load(path).Bookmarks).Note);
        File.WriteAllText(SessionAnnotationStore.PathFor(path), "{");
        Assert.Throws<InvalidDataException>(() => SessionAnnotationStore.Load(path));
    }

    [Fact]
    public void Analysis_RejectsUnitMismatch_AndHandlesConstantAndMissingPairs()
    {
        var a = new MetricSeries(Cpu, [Start, Start.AddSeconds(1), Start.AddSeconds(2)], [1f, null, 1f]);
        var b = new MetricSeries(Cpu, [Start, Start.AddSeconds(1), Start.AddSeconds(2)], [2f, 3f, 2f]);
        var correlation = SessionAnalysis.Correlate(a, b);
        Assert.Equal(2, correlation.PairCount); Assert.Null(correlation.Pearson);
        var first = new SessionData(Start, null, false, 3, [a], []);
        var incompatible = new SessionData(Start, null, false, 3, [new MetricSeries(Cpu with { Unit = "ms" }, a.TimesUtc, a.Values)], []);
        Assert.Empty(SessionAnalysis.Compare(first, incompatible));
        Assert.Equal(2.0 / 3, SessionAnalysis.Compare(a, b).Coverage, 3);
    }

    [Fact]
    public void Analysis_UsesSharedElapsedDuration_NotSampleIndexCadence()
    {
        var slow = new MetricSeries(Cpu, [Start, Start.AddSeconds(2), Start.AddSeconds(4)], [10f, 10f, 10f]);
        var fast = new MetricSeries(Cpu, [Start, Start.AddSeconds(1), Start.AddSeconds(2), Start.AddSeconds(3), Start.AddSeconds(4), Start.AddSeconds(5)], [20f, 20f, 20f, 20f, 20f, 999f]);
        var a = new SessionData(Start, Start.AddSeconds(4), true, 3, [slow], []);
        var b = new SessionData(Start, Start.AddSeconds(5), true, 6, [fast], []);
        var result = Assert.Single(SessionAnalysis.Compare(a, b));
        Assert.Equal(4, result.DurationSeconds); Assert.Equal(3, result.FirstCount); Assert.Equal(5, result.SecondCount); Assert.Equal(10f, result.Delta);
        var late = new SessionData(Start, Start.AddSeconds(4), true, 1, [new MetricSeries(Cpu, [Start.AddSeconds(10)], [2f])], []);
        var noOverlap = Assert.Single(SessionAnalysis.Compare(a, late));
        Assert.Null(noOverlap.Delta); Assert.Equal(0, noOverlap.FirstCount == 0 || noOverlap.SecondCount == 0 ? 0 : 1);
    }

    [Fact]
    public async Task BookmarkOutsideWindow_LoadsItsContainingWindow()
    {
        var path = Write([1f, 2f, 3f, 4f]);
        using var recorder = new SessionRecorder(_directory);
        var vm = new Stats.Core.ViewModels.SessionViewModel(recorder, () => []);
        Assert.True(vm.Open(path));
        await vm.LoadReplayWindowAsync(0, 2);
        vm.Bookmarks.Add(new(3, "last"));
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.JumpToBookmarkCommand).ExecuteAsync(vm.Bookmarks[0]);
        Assert.Equal(3, vm.ReplayWindowStart + vm.ReplayIndex);
    }

    [Fact]
    public void NewlineTerminatedMalformedTailThrows_ButUnterminatedTailRecovers()
    {
        var path = Write([1f]);
        File.WriteAllLines(path, File.ReadAllLines(path).Take(2));
        File.AppendAllText(path, "{\"type\":\"sample\"\n");
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => SessionFile.Load(path));
        File.WriteAllText(path, File.ReadAllText(path).TrimEnd('\n') + "{\"type\":\"sample\"");
        Assert.Equal(1, SessionFile.Load(path).SampleCount);
    }

    [Fact]
    public void HoverCursorSnapsReadoutsToNearestSample()
    {
        var path = Write([10f, 20f, 30f]);
        using var recorder = new SessionRecorder(_directory);
        var vm = new Stats.Core.ViewModels.SessionViewModel(recorder, () => []);
        Assert.True(vm.Open(path));
        vm.ReplayCursorUtc = Start.AddSeconds(.9);
        Assert.Equal(1, vm.ReplayIndex); Assert.Equal(Start.AddSeconds(1), vm.ReplayCursorUtc);
        Assert.Contains("20", vm.Rows[0].ReplayValue);
    }

    private string Write(float?[] values)
    {
        Directory.CreateDirectory(_directory); var path = Path.Combine(_directory, "x.stats-session.jsonl");
        using var recorder = new SessionRecorder(_directory, () => Start.AddSeconds(values.Length));
        recorder.Start([Cpu], Start);
        for (var i = 0; i < values.Length; i++) recorder.Record(new(new Dictionary<string, float?> { ["cpu"] = values[i] }, Start.AddSeconds(i)));
        recorder.StopAsync().GetAwaiter().GetResult(); return recorder.FilePath!;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
