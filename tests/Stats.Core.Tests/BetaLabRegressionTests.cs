using Stats.Core.ViewModels;
using Stats.Core.Lab;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.Recording;
using System.Text.Json;

namespace Stats.Core.Tests;

public sealed class BetaLabRegressionTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.BetaLab", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly MetricDefinition Cpu = new("cpu", "CPU", MetricGroup.Cpu, "private hardware", "%");

    [Fact]
    public void AutomationDisablingStopsOwnedRecordingButNeverManual()
    {
        using var vm = new LabViewModel(_directory);
        vm.Options.AutoGamingEnabled = true;
        vm.Games.Add(new() { ExecutableBaseName = "game", AutoRecord = true });
        var requests = new List<bool>();
        vm.RecordingRequested += (start, _) => { requests.Add(start); vm.AcknowledgeRecording(start, start); };
        vm.SetManualRecording(true);
        vm.ObserveGaming("game", 60, Start); vm.ObserveGaming("game", 60, Start.AddSeconds(5));
        Assert.Empty(requests);
        vm.Options.AutoGamingEnabled = false; vm.ObserveGaming(null, null, Start.AddSeconds(6));
        Assert.Empty(requests);
        vm.SetManualRecording(false); vm.Options.AutoGamingEnabled = true;
        vm.ObserveGaming("game", 60, Start.AddSeconds(7)); vm.ObserveGaming("game", 60, Start.AddSeconds(12));
        vm.Options.AutoGamingEnabled = false; vm.ObserveGaming(null, null, Start.AddSeconds(13));
        Assert.Equal(new[] { true, false }, requests);
    }

    [Fact]
    public void CompoundUsesUnselectedOperandsAndRaisesOncePerEpisode()
    {
        using var vm = new LabViewModel(_directory);
        vm.Rules.Add(new() { Enabled = true, FirstMetricId = "a", SecondMetricId = "b", HoldSeconds = 1 });
        var notices = new List<LabTimelineEvent>(); vm.CompoundNotificationRequested += notices.Add;
        var operands = new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 };
        for (var i = 0; i < 20; i++) vm.ObserveSnapshot(new Dictionary<string, float?>(), Start.AddSeconds(i), ruleValues: operands);
        Assert.Single(notices);
    }

    [Fact]
    public void HistoryRetentionIsInclusiveAndPreservesSiblingRecordings()
    {
        var history = Path.Combine(_directory, "compact-history"); Directory.CreateDirectory(history);
        var recordings = Path.Combine(_directory, "recordings"); Directory.CreateDirectory(recordings);
        var preserved = Path.Combine(recordings, "20200101.lab-history.jsonl"); File.WriteAllText(preserved, "keep");
        var removed = Path.Combine(history, "20260908.lab-history.jsonl"); File.WriteAllText(removed, "old");
        var kept = Path.Combine(history, "20260909.lab-history.jsonl"); File.WriteAllText(kept, "keep");
        using (var writer = new CompactHistory(_directory)) writer.Record(new Dictionary<string, float?> { ["cpu"] = 10 }, Start, 7, 16);
        Assert.False(File.Exists(removed)); Assert.True(File.Exists(kept)); Assert.Equal("keep", File.ReadAllText(preserved));
    }

    [Fact]
    public void HistoryRejectsCorruptInteriorButRecoversUnterminatedTail()
    {
        var directory = Path.Combine(_directory, "compact-history"); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "20260915.lab-history.jsonl");
        var valid = JsonSerializer.Serialize(new HistoryAggregate(Start, "cpu", 1, 10, 10, 10));
        File.WriteAllText(path, valid + "\n{");
        using var history = new CompactHistory(_directory);
        Assert.Single(history.ReadRecent());
        File.WriteAllText(path, "{\n" + valid + "\n");
        Assert.Throws<InvalidDataException>(() => history.ReadRecent());
    }

    [Fact]
    public void AbDisjointMissingBinsDoNotClaimCoverage()
    {
        var times = new[] { Start, Start.AddSeconds(1), Start.AddSeconds(2), Start.AddSeconds(3) };
        var a = new MetricSeries(Cpu, times, [10, null, 30, null]);
        var b = new MetricSeries(Cpu, times, [null, 20, null, 40]);
        var comparison = SessionAnalysis.Compare(a, b);
        Assert.Equal(0, comparison.PairCount); Assert.Equal(0, comparison.Coverage);
        Assert.Null(comparison.Delta); Assert.Equal(.5, comparison.FirstCoverage);
    }

    [Fact]
    public void OldSettingsAndInvalidNewCollectionsPreserveSelections()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "settings.json"), """{"DashboardMetrics":[null,"","personal-id"],"Scenes":null,"ThemeDesigns":[null],"ThemeSecondary":"invalid"}""");
        var settings = new SettingsService(_directory).Load();
        Assert.Equal("personal-id", Assert.Single(settings.DashboardMetrics)); Assert.Empty(settings.Scenes);
        Assert.Empty(settings.ThemeDesigns); Assert.Null(settings.ThemeSecondary);
    }

    [Fact]
    public void LongRun_KeepsTimelineBounded_AndHistoryOffCreatesNoDirectory()
    {
        using var vm = new LabViewModel(_directory);
        var start = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 100_000; i++) vm.AddTimeline("tick", "x", start.AddSeconds(i));
        Assert.Equal(500, vm.Timeline.Count);
        Assert.False(Directory.Exists(Path.Combine(_directory, "compact-history")));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
