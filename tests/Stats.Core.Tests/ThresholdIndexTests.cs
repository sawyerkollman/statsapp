using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

/// <summary>ThresholdIndex must resolve rule/override/none identically to ThresholdEvaluator (which stays the
/// unit under test for the actual Warn/Crit comparison — see ThresholdEvaluatorTests) across groups, units, and
/// direction (LowerIsWorse).</summary>
public class ThresholdIndexTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition CpuClock = new("cpu.clock", "Core #1", MetricGroup.Cpu, "CPU", "MHz");
    private static readonly MetricDefinition Fps = new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps", "F0");
    private static readonly MetricDefinition Mobo = new("mobo.temp", "Motherboard", MetricGroup.Motherboard, "Board", "°C", "F1");

    private static AppSettings Seeded() => new() { ThresholdRules = ThresholdDefaults.Rules() };

    [Theory]
    [InlineData(84.9f, Severity.Normal)]
    [InlineData(85f, Severity.Warn)]
    [InlineData(91.9f, Severity.Warn)]
    [InlineData(92f, Severity.Crit)]
    public void Evaluate_GroupUnitRule_MatchesThresholdEvaluator(float value, Severity expected)
    {
        var settings = Seeded();
        var index = ThresholdIndex.Build(settings);
        Assert.Equal(expected, index.Evaluate(CpuTemp, value));
        Assert.Equal(ThresholdEvaluator.Evaluate(CpuTemp, value, settings), index.Evaluate(CpuTemp, value));
    }

    [Theory]
    [InlineData(61f, Severity.Normal)]
    [InlineData(60f, Severity.Warn)]
    [InlineData(30f, Severity.Crit)]
    public void Evaluate_LowerIsWorseRule_MatchesThresholdEvaluator(float value, Severity expected)
    {
        var settings = Seeded();
        var index = ThresholdIndex.Build(settings);
        Assert.Equal(expected, index.Evaluate(Fps, value));
        Assert.Equal(ThresholdEvaluator.Evaluate(Fps, value, settings), index.Evaluate(Fps, value));
    }

    [Fact]
    public void RuleFor_NoMatchingRule_ReturnsNull_MatchesThresholdEvaluator()
    {
        var settings = Seeded();
        var index = ThresholdIndex.Build(settings);
        Assert.Null(index.RuleFor(CpuClock));
        Assert.Equal(ThresholdEvaluator.RuleFor(CpuClock, settings), index.RuleFor(CpuClock));
        Assert.Equal(Severity.Normal, index.Evaluate(CpuClock, 5000f));
    }

    [Fact]
    public void RuleFor_Override_BeatsGroupRule_MatchesThresholdEvaluator()
    {
        var settings = Seeded();
        settings.ThresholdOverrides["cpu.temp"] = new ThresholdRule { Warn = 60, Crit = 70 };
        var index = ThresholdIndex.Build(settings);
        Assert.Same(settings.ThresholdOverrides["cpu.temp"], index.RuleFor(CpuTemp));
        Assert.Equal(Severity.Crit, index.Evaluate(CpuTemp, 75f));
        Assert.Equal(ThresholdEvaluator.Evaluate(CpuTemp, 75f, settings), index.Evaluate(CpuTemp, 75f));
    }

    [Fact]
    public void RuleFor_MotherboardGroup_MatchesThresholdEvaluator()
    {
        var settings = Seeded();
        var index = ThresholdIndex.Build(settings);
        Assert.Equal(ThresholdEvaluator.RuleFor(Mobo, settings)?.Warn, index.RuleFor(Mobo)?.Warn);
        Assert.Equal(ThresholdEvaluator.Evaluate(Mobo, 96f, settings), index.Evaluate(Mobo, 96f));
    }

    [Fact]
    public void Evaluate_WarnEqualsCrit_Inactive_MatchesThresholdEvaluator()
    {
        var settings = new AppSettings { ThresholdRules = { new ThresholdRule { Group = MetricGroup.Motherboard, Unit = "°C", Warn = 0, Crit = 0 } } };
        var index = ThresholdIndex.Build(settings);
        Assert.Equal(Severity.Normal, index.Evaluate(Mobo, 500f));
        Assert.Equal(ThresholdEvaluator.Evaluate(Mobo, 500f, settings), index.Evaluate(Mobo, 500f));
    }

    [Fact]
    public void Evaluate_NullOrNaN_Normal()
    {
        var index = ThresholdIndex.Build(Seeded());
        Assert.Equal(Severity.Normal, index.Evaluate(CpuTemp, null));
        Assert.Equal(Severity.Normal, index.Evaluate(CpuTemp, float.NaN));
    }

    [Fact]
    public void Build_SnapshotsSettings_LaterMutationDoesNotAffectAlreadyBuiltIndex()
    {
        var settings = Seeded();
        var index = ThresholdIndex.Build(settings);
        settings.ThresholdOverrides["cpu.temp"] = new ThresholdRule { Warn = 1, Crit = 2 };
        // The already-built index still resolves the group rule, not the override added afterward.
        Assert.Equal(Severity.Normal, index.Evaluate(CpuTemp, 10f));
    }
}
