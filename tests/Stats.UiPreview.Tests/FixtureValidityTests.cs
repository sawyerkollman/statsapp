using Stats.Core.Fans;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Validates every scenario's fixture data against the actual model constraints (PREVIEW_HARNESS.md:
/// "Validate fixtures against the actual model constraints") — never against a live device.</summary>
public class FixtureValidityTests
{
    public static IEnumerable<object[]> ScenarioNames => Scenarios.Names.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void DashboardAndOverlayMetricIds_ExistInDefinitions(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        var ids = fixture.Definitions.Select(d => d.Id).ToHashSet();
        foreach (var id in fixture.DashboardMetrics) Assert.Contains(id, ids);
        foreach (var id in fixture.OverlayMetrics) Assert.Contains(id, ids);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void ThresholdOverrideIds_ExistInDefinitions(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        var ids = fixture.Definitions.Select(d => d.Id).ToHashSet();
        foreach (var id in fixture.ThresholdOverrides.Keys) Assert.Contains(id, ids);
        foreach (var id in fixture.MetricLimits.Keys) Assert.Contains(id, ids);
        foreach (var id in fixture.TilePrefs.Keys) Assert.Contains(id, ids);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void FanChannelSourceIds_ExistInDefinitions(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        var ids = fixture.Definitions.Select(d => d.Id).ToHashSet();
        foreach (var ch in fixture.FanChannels)
        {
            if (ch.RpmMetricId is not null) Assert.Contains(ch.RpmMetricId, ids);
            if (ch.PercentMetricId is not null) Assert.Contains(ch.PercentMetricId, ids);
        }
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void EveryFanCurve_PassesFanCurveTryCreate(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        foreach (var pref in fixture.FanChannelPrefs.Values)
            Assert.True(FanCurve.TryCreate(pref.Points, out _), "A fixture fan channel's curve points failed FanCurve.TryCreate.");
        foreach (var profile in fixture.FanProfiles)
            foreach (var pref in profile.Channels.Values)
                Assert.True(FanCurve.TryCreate(pref.Points, out _), $"Profile '{profile.Name}' has an invalid curve.");
        // The default curve every real channel starts from (no explicit FanChannelPref) must also be valid.
        Assert.True(FanCurve.TryCreate(FanCurve.DefaultPoints, out _));
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void EveryDefinitionId_IsUnique(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        var ids = fixture.Definitions.Select(d => d.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void EveryHistory_HasExpectedSampleCount(string scenario)
    {
        var fixture = Scenarios.Build(scenario);
        var store = new MetricStore(fixture.Definitions, capacity: 120);
        foreach (var tick in fixture.Ticks) store.Apply(tick);

        // The ring buffer is capacity-120 and every scenario applies TimeSeries.Ticks (60) samples — every
        // metric's buffered sample count must equal exactly that, gaps (NaN) included.
        foreach (var def in fixture.Definitions)
        {
            Assert.True(store.TryGet(def.Id, out var history), $"No history for {def.Id}");
            Assert.Equal(fixture.Ticks.Count, history.ToArray().Length);
        }
    }

    [Fact]
    public void ThresholdsScenario_EvaluatesToIntendedSeverities()
    {
        var fixture = Scenarios.Build("thresholds");
        var settings = new AppSettings();
        foreach (var (id, rule) in fixture.ThresholdOverrides) settings.ThresholdOverrides[id] = rule;
        ThresholdDefaults.EnsureDefaults(settings.ThresholdRules, settings.ThresholdOverrides);

        var cpuTemp = fixture.Definitions.Single(d => d.Id == "cpu.temp.tctl");
        var cpuNormal = fixture.Definitions.Single(d => d.Id == "cpu.temp.core0");
        var gpuTemp = fixture.Definitions.Single(d => d.Id == "gpu.temp.core");
        var fpsAvg = fixture.Definitions.Single(d => d.Id == FrameMetrics.FpsId);
        var fpsLow = fixture.Definitions.Single(d => d.Id == FrameMetrics.LowId);

        Assert.Equal(Severity.Warn, ThresholdEvaluator.Evaluate(cpuTemp, 88f, settings));   // 85 <= 88 < 92
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(cpuNormal, 54f, settings));
        Assert.Equal(Severity.Crit, ThresholdEvaluator.Evaluate(gpuTemp, 90f, settings));    // >= 88

        // Default inverted Game/fps rule is 60/30 (lower is worse): 45 -> Warn, 25 -> Crit — evaluated directly at
        // both spec-called-out values, independent of whatever the fixture's own "current" tick happens to show.
        Assert.Equal(Severity.Warn, ThresholdEvaluator.Evaluate(fpsAvg, 45f, settings));
        Assert.Equal(Severity.Crit, ThresholdEvaluator.Evaluate(fpsAvg, 25f, settings));

        // 1% low has its own override (30/15, lower is worse), distinct from the 60/30 fps.avg rule.
        Assert.Equal(Severity.Warn, ThresholdEvaluator.Evaluate(fpsLow, 20f, settings));
        Assert.Equal(Severity.Crit, ThresholdEvaluator.Evaluate(fpsLow, 12f, settings));
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(fpsLow, 45f, settings));
    }

    [Fact]
    public void MissingScenario_CurrentIsNull_AndHealthReportsFailure()
    {
        var fixture = Scenarios.Build("missing");
        var store = new MetricStore(fixture.Definitions, capacity: 120);
        foreach (var tick in fixture.Ticks) store.Apply(tick);

        Assert.True(store.TryGet("cpu.temp.tctl", out var h));
        Assert.Null(h.Current);
        Assert.NotNull(fixture.Health);
        Assert.False(fixture.Health!.IsHealthy);
        Assert.True(fixture.Health.ConsecutiveFailures >= 3);
        Assert.NotEmpty(fixture.Health.FailingBackends);
        Assert.True(fixture.Degraded);
        Assert.NotNull(fixture.GameStatus);
    }

    [Fact]
    public void EmptyScenario_HasNoDashboardOrOverlaySelectionAndNoFanChannels()
    {
        var fixture = Scenarios.Build("empty");
        Assert.Empty(fixture.DashboardMetrics);
        Assert.Empty(fixture.OverlayMetrics);
        Assert.Empty(fixture.FanChannels);
    }

    [Fact]
    public void DenseScenario_HasSixteenCoreMatrixCandidatesAndEdgeCaseValues()
    {
        var fixture = Scenarios.Build("dense");
        var coreLoads = fixture.Definitions.Count(d => d.Group == MetricGroup.Cpu && d.Unit == "%" && d.DisplayName.StartsWith("Core #", StringComparison.Ordinal));
        Assert.Equal(16, coreLoads);

        var store = new MetricStore(fixture.Definitions, capacity: 120);
        foreach (var tick in fixture.Ticks) store.Apply(tick);
        Assert.Contains(fixture.Definitions, d => store.TryGet(d.Id, out var h) && h.Current == 0f);
        Assert.Contains(fixture.Definitions, d => store.TryGet(d.Id, out var h) && h.Current == 100f);
        Assert.Contains(fixture.Definitions, d => store.TryGet(d.Id, out var h) && h.Current is float v && v < 0f); // negative temperature
        Assert.Contains(fixture.Definitions, d => d.Unit == "W" && store.TryGet(d.Id, out var h) && h.Current is float w && w > 400f); // high wattage
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void GameGroupUsesRealFrameMetricsIdentities(string scenario)
    {
        // Not every scenario needs Game-group metrics (e.g. "fans" and "gallery" focus elsewhere) — but any that
        // do exist must be the real Stats.Core.Frames.FrameMetrics identities, in the Game group.
        var fixture = Scenarios.Build(scenario);
        var frameDefs = fixture.Definitions.Where(d => FrameMetrics.IsFrameMetric(d.Id)).ToList();
        foreach (var def in frameDefs) Assert.Equal(MetricGroup.Game, def.Group);
        if (scenario is not ("fans" or "gallery")) Assert.NotEmpty(frameDefs);
    }
}
