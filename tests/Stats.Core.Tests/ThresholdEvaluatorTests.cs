using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class ThresholdEvaluatorTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition CpuClock = new("cpu.clock", "Core #1", MetricGroup.Cpu, "CPU", "MHz");

    private static AppSettings Seeded() => new() { ThresholdRules = ThresholdDefaults.Rules() };

    [Theory]
    [InlineData(84.9f, Severity.Normal)]
    [InlineData(85f, Severity.Warn)]
    [InlineData(91.9f, Severity.Warn)]
    [InlineData(92f, Severity.Crit)]
    public void Evaluate_UsesGroupUnitRule(float value, Severity expected) =>
        Assert.Equal(expected, ThresholdEvaluator.Evaluate(CpuTemp, value, Seeded()));

    [Fact]
    public void Evaluate_OverrideBeatsRule()
    {
        var s = Seeded();
        s.ThresholdOverrides["cpu.temp"] = new ThresholdRule { Warn = 60, Crit = 70 };
        Assert.Equal(Severity.Crit, ThresholdEvaluator.Evaluate(CpuTemp, 75f, s));
    }

    [Fact]
    public void Evaluate_NoMatchingRule_Normal() =>
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(CpuClock, 5000f, Seeded()));

    [Fact]
    public void Evaluate_NullOrNaN_Normal()
    {
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(CpuTemp, null, Seeded()));
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(CpuTemp, float.NaN, Seeded()));
    }

    private static readonly MetricDefinition Fps = new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps", "F0");

    [Theory]
    [InlineData(61f, Severity.Normal)]
    [InlineData(60f, Severity.Warn)]
    [InlineData(59.9f, Severity.Warn)]
    [InlineData(30.1f, Severity.Warn)]
    [InlineData(30f, Severity.Crit)]
    [InlineData(5f, Severity.Crit)]
    public void Evaluate_LowerIsWorse_InvertsComparison(float value, Severity expected) =>
        Assert.Equal(expected, ThresholdEvaluator.Evaluate(Fps, value, Seeded()));

    [Fact]
    public void Defaults_ContainFpsRule_LowerIsWorse()
    {
        var r = ThresholdDefaults.Rules().Single(x => x.Group == MetricGroup.Game && x.Unit == "fps");
        Assert.True(r.LowerIsWorse);
        Assert.Equal(60f, r.Warn);
        Assert.Equal(30f, r.Crit);
    }

    [Fact]
    public void EnsureDefaults_AddsOnlyMissingPairs_KeepsUserValues()
    {
        var rules = new List<ThresholdRule> { new() { Group = MetricGroup.Cpu, Unit = "°C", Warn = 70, Crit = 80 } };
        ThresholdDefaults.EnsureDefaults(rules);
        Assert.Equal(70f, rules.Single(r => r.Group == MetricGroup.Cpu && r.Unit == "°C").Warn); // untouched
        Assert.Contains(rules, r => r.Group == MetricGroup.Game && r.Unit == "fps" && r.LowerIsWorse);
        Assert.Equal(ThresholdDefaults.Rules().Count, rules.Count);
        int n = rules.Count;
        ThresholdDefaults.EnsureDefaults(rules);
        Assert.Equal(n, rules.Count); // idempotent
    }

    [Fact]
    public void Defaults_ContainMotherboardRule()
    {
        var r = ThresholdDefaults.Rules().Single(x => x.Group == MetricGroup.Motherboard && x.Unit == "°C");
        Assert.False(r.LowerIsWorse);
        Assert.Equal(80f, r.Warn);
        Assert.Equal(95f, r.Crit);
    }

    [Fact]
    public void EnsureDefaults_OldFileWithoutMotherboardRule_GainsIt()
    {
        // Simulates a pre-v1.8 settings.json: no Motherboard rule at all.
        var rules = new List<ThresholdRule>
        {
            new() { Group = MetricGroup.Cpu, Unit = "°C", Warn = 85, Crit = 92 },
            new() { Group = MetricGroup.Gpu, Unit = "°C", Warn = 80, Crit = 88 },
            new() { Group = MetricGroup.Cpu, Unit = "%", Warn = 90, Crit = 98 },
            new() { Group = MetricGroup.Gpu, Unit = "%", Warn = 90, Crit = 98 },
        };
        ThresholdDefaults.EnsureDefaults(rules);
        var mobo = rules.Single(r => r.Group == MetricGroup.Motherboard && r.Unit == "°C");
        Assert.Equal(80f, mobo.Warn);
        Assert.Equal(95f, mobo.Crit);
    }

    [Fact]
    public void Evaluate_WarnEqualsCrit_RuleIsInactive_Normal()
    {
        // A freshly "Add rule…"-ed rule seeds Warn = Crit = 0; without this guard every non-negative reading
        // (i.e. almost every reading) would immediately show Crit before the user ever sets real thresholds.
        var mobo = new MetricDefinition("mobo.temp", "Motherboard", MetricGroup.Motherboard, "Board", "°C", "F1");
        var s = new AppSettings { ThresholdRules = { new ThresholdRule { Group = MetricGroup.Motherboard, Unit = "°C", Warn = 0, Crit = 0 } } };
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(mobo, 0f, s));
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(mobo, 50f, s));
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(mobo, 500f, s));

        // Also inactive for a lower-is-worse 0/0 rule (e.g. fps seeded blank).
        var fps = new MetricDefinition("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps", "F0");
        var s2 = new AppSettings { ThresholdRules = { new ThresholdRule { Group = MetricGroup.Game, Unit = "fps", Warn = 0, Crit = 0, LowerIsWorse = true } } };
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(fps, 0f, s2));
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(fps, 100f, s2));
    }

    private static readonly MetricDefinition FpsLow = new("fps.low1", "1% Low FPS", MetricGroup.Game, "Foreground app", "fps", "F0");

    [Fact]
    public void EnsureDefaults_GivesTheOnePercentLow_ItsOwnScale()
    {
        // Same (Group, Unit) as fps.avg, so the 60/30 group rule would paint the 1 % Low tile Warn — and often
        // Crit — permanently on a machine that is gaming perfectly happily.
        var s = new AppSettings { ThresholdRules = new() };
        ThresholdDefaults.EnsureDefaults(s.ThresholdRules, s.ThresholdOverrides);
        Assert.Equal(Severity.Normal, ThresholdEvaluator.Evaluate(FpsLow, 45f, s));
        Assert.Equal(Severity.Warn, ThresholdEvaluator.Evaluate(Fps, 45f, s));
        Assert.Equal(Severity.Warn, ThresholdEvaluator.Evaluate(FpsLow, 30f, s));
        Assert.Equal(Severity.Crit, ThresholdEvaluator.Evaluate(FpsLow, 15f, s));

        var kept = new AppSettings { ThresholdRules = new() };
        kept.ThresholdOverrides["fps.low1"] = new ThresholdRule { Warn = 20, Crit = 10, LowerIsWorse = true };
        ThresholdDefaults.EnsureDefaults(kept.ThresholdRules, kept.ThresholdOverrides);
        Assert.Equal(20f, kept.ThresholdOverrides["fps.low1"].Warn); // never overwrites the user's own
    }
}
