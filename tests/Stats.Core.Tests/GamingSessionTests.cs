using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public sealed class GamingSessionTests : IDisposable
{
    [Fact]
    public void RequestedWindowCountsMissingEdgesAndAbsentRuns()
    {
        var run = new SessionData(Start, Start.AddSeconds(3), true, 2,
            [new(Frame, [Start.AddSeconds(2), Start.AddSeconds(3)], [10f, 20f])], []);
        var empty = new SessionData(Start, Start.AddSeconds(10), true, 0, [], []);
        var comparison = Assert.Single(SessionAnalysis.CompareWindow(run, run, 10));
        Assert.Equal(10, comparison.DurationSeconds);
        Assert.Equal(2d / 11, comparison.Coverage, 6);
        var variation = Assert.Single(SessionAnalysis.Variation([run, empty], 10));
        Assert.Equal(1, variation.Runs);
        Assert.Equal(2, variation.TotalRuns);
        Assert.Equal(1d / 11, variation.Coverage, 6);
        Assert.Null(variation.StandardDeviation);
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionAnalysis.Variation([run], double.NaN));
    }
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.GamingSession", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 17, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Frame = new("fps.frametime", "Frame time", MetricGroup.Game, "Game", "ms");

    [Fact]
    public async Task ElapsedWindow_IsBounded_AndReportLabelsSamples()
    {
        using var recorder = new SessionRecorder(_directory, () => Start.AddSeconds(5));
        recorder.Start([Frame], Start);
        for (var i = 0; i < 5; i++) recorder.Record(new(new Dictionary<string, float?> { [Frame.Id] = i + 1 }, Start.AddSeconds(i)));
        await recorder.StopAsync();
        var window = SessionFile.LoadElapsedWindow(recorder.FilePath!, 1, 2);
        Assert.Equal(new float?[] { 2, 3, 4 }, window.Data.Series[0].Values);
        Assert.False(window.IsTruncated);
        Assert.Contains("not true per-frame", SessionReports.Build(window.Data).Text);
    }

    [Fact]
    public async Task FailedComparisonClearsPreviousResultAndParametersInvalidateIt()
    {
        using var recorder = new SessionRecorder(_directory, () => Start.AddSeconds(5));
        recorder.Start([Frame], Start);
        for (var i = 0; i < 5; i++) recorder.Record(new(new Dictionary<string, float?> { [Frame.Id] = i + 1 }, Start.AddSeconds(i)));
        await recorder.StopAsync();
        var vm = new Stats.Core.ViewModels.SessionViewModel(recorder, () => [Frame]);
        Assert.True(vm.Open(recorder.FilePath!));
        Assert.True(await vm.OpenComparisonAsync(recorder.FilePath!));
        Assert.Contains("paired", vm.AbText);
        Assert.False(await vm.OpenComparisonAsync(Path.Combine(_directory, "missing.stats-session.jsonl")));
        Assert.DoesNotContain("paired", vm.AbText);
        Assert.Contains("unavailable", vm.AbText);
        vm.ComparisonDurationSeconds = 20;
        Assert.Contains("Window changed", vm.AbText);
    }

    [Fact]
    public void ReportLabelsScopeAndIncludesConcurrentContext()
    {
        var temperature = new MetricDefinition("cpu.temp", "CPU temperature", MetricGroup.Cpu, "CPU", "°C");
        var data = new SessionData(Start, Start.AddMinutes(5), true, 300,
            [new(Frame, [Start], [40f]), new(temperature, [Start], [70f])],
            [new(Frame.Id, 10, 20, 40), new(temperature.Id, 60, 65, 70)]);
        var report = SessionReports.Build(data, 100, windowSummaries: true);
        Assert.Contains("Full recording", report.Text);
        Assert.Contains("starting at sample 101", report.Text);
        Assert.Contains("this replay window only", report.Text);
        Assert.Contains("CPU temperature", Assert.Single(report.Spikes).Details);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void ElapsedWindow_RejectsInvalidBounds_AndVariationReportsSpread()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionFile.LoadElapsedWindow("x", -1, 1));
        var a = new SessionData(Start, Start.AddSeconds(2), true, 2, [new(Frame, [Start, Start.AddSeconds(2)], [10f, null])], []);
        var b = new SessionData(Start, Start.AddSeconds(1), true, 2, [new(Frame, [Start, Start.AddSeconds(1)], [20f, 30f])], []);
        var variation = Assert.Single(SessionAnalysis.Variation([a, b]));
        Assert.Equal(2, variation.Runs); Assert.Equal(17.5f, variation.Mean); Assert.Equal(10f, variation.Minimum); Assert.Equal(25f, variation.Maximum);
        Assert.Equal(.75, variation.Coverage, 3);
    }
}
