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
}
