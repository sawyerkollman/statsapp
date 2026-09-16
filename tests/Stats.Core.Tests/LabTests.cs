using Stats.Core.Lab;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class LabTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Stats.LabTests", Guid.NewGuid().ToString("N"));
    private static readonly DateTime Start = new(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Gaming_DefaultOffHasNoEvents_ThenEntersAndExitsExactlyOnce()
    {
        using var vm = new LabViewModel(_directory); var games = new List<bool>(); vm.GameStateChanged += (on, _) => games.Add(on);
        for (var i = 0; i < 60; i++) vm.ObserveGaming("game", 60, Start.AddSeconds(i));
        Assert.Empty(games);
        vm.Options.AutoGamingEnabled = true;
        for (var i = 0; i < 60; i++) vm.ObserveGaming("game", 60, Start.AddSeconds(i));
        for (var i = 60; i < 90; i++) vm.ObserveGaming(null, null, Start.AddSeconds(i));
        Assert.Equal(new[] { true, false }, games);
    }

    [Fact]
    public void Compound_MissingResetsAndTestOnlySuppressesNotification()
    {
        using var vm = new LabViewModel(_directory); var notices = 0; vm.CompoundNotificationRequested += _ => notices++;
        vm.Rules.Add(new CompoundRule { Enabled = true, TestOnly = true, FirstMetricId = "a", SecondMetricId = "b", FirstThreshold = 1, SecondThreshold = 1, HoldSeconds = 5 });
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 }, Start);
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 2 }, Start.AddSeconds(4));
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 }, Start.AddSeconds(10));
        for (var i = 11; i <= 15; i++) vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 }, Start.AddSeconds(i));
        Assert.Equal(0, notices); Assert.Contains(vm.Timeline, e => e.Kind == "test");
    }

    [Fact]
    public void Compound_LongObservationGapDoesNotCountAsHold()
    {
        var evaluator = new CompoundEvaluator();
        var rule = new CompoundRule { Enabled = true, FirstMetricId = "a", SecondMetricId = "b", FirstThreshold = 1, SecondThreshold = 1, HoldSeconds = 5 };
        var values = new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 };
        Assert.Empty(evaluator.Tick([rule], values, Start));
        Assert.Empty(evaluator.Tick([rule], values, Start.AddSeconds(60)));
        Assert.Empty(evaluator.Tick([rule], values, Start.AddSeconds(62)));
        Assert.Single(evaluator.Tick([rule], values, Start.AddSeconds(65)));
    }

    [Fact]
    public void Compound_FiveSecondCadenceSatisfiesFiveSecondHold()
    {
        var evaluator = new CompoundEvaluator();
        var rule = new CompoundRule { Enabled = true, FirstMetricId = "a", SecondMetricId = "b", FirstThreshold = 1, SecondThreshold = 1, HoldSeconds = 5 };
        var values = new Dictionary<string, float?> { ["a"] = 2, ["b"] = 2 };
        Assert.Empty(evaluator.Tick([rule], values, Start));
        Assert.Single(evaluator.Tick([rule], values, Start.AddSeconds(5), pollSeconds: 5));
    }

    [Fact]
    public async Task RestartHistory_AfterBackwardsClock_AllowsFreshRecords()
    {
        using var vm = new LabViewModel(_directory);
        vm.Options.HistoryEnabled = true;
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 1 }, Start);
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 2 }, Start.AddMinutes(-1));
        Assert.StartsWith("History", vm.Error);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.RestartHistoryCommand).ExecuteAsync(null);
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 3 }, Start.AddMinutes(1));
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 4 }, Start);
        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)vm.RestartHistoryCommand).ExecuteAsync(null);
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 5 }, Start.AddMinutes(2));
        Assert.DoesNotContain("History", vm.Error);
        await vm.RestartHistoryCommand.ExecuteAsync(null);
        vm.ObserveSnapshot(new Dictionary<string, float?> { ["a"] = 4 }, Start.AddMinutes(2));
        vm.Dispose();
        using var reader = new CompactHistory(_directory);
        Assert.Contains(reader.ReadRecent(), r => r.MinuteUtc == Start.AddMinutes(2) && r.Average == 4);
    }

    [Fact]
    public void Diagnostics_ExcludeMetricAndHardwareNames()
    {
        var metric = new MetricDefinition("personal.id", "Personal CPU", MetricGroup.Cpu, "Secret hardware", "%");
        using var vm = new LabViewModel(_directory, [metric]); vm.SetDiagnostics("1", "os", ".net", true);
        Assert.DoesNotContain("personal.id", vm.DiagnosticPreview); Assert.DoesNotContain("Secret hardware", vm.DiagnosticPreview);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
