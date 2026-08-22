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
}
