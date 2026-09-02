using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class ThresholdInputTests
{
    [Fact]
    public void TryParse_ValidOrderedPair_ReturnsRule()
    {
        var ok = ThresholdInput.TryParse("85", "92", lowerIsWorse: false, out var rule, out var error);
        Assert.True(ok);
        Assert.Equal(85f, rule.Warn);
        Assert.Equal(92f, rule.Crit);
        Assert.False(rule.LowerIsWorse);
        Assert.Equal("", error);
    }

    [Fact]
    public void TryParse_ValidLowerIsWorsePair_ReturnsRule()
    {
        var ok = ThresholdInput.TryParse("60", "30", lowerIsWorse: true, out var rule, out var error);
        Assert.True(ok);
        Assert.Equal(60f, rule.Warn);
        Assert.Equal(30f, rule.Crit);
        Assert.True(rule.LowerIsWorse);
        Assert.Equal("", error);
    }

    [Theory]
    [InlineData("abc", "92")]
    [InlineData("", "92")]
    [InlineData(" ", "92")]
    public void TryParse_UnparseableWarn_FailsWithMessage(string warnText, string critText)
    {
        var ok = ThresholdInput.TryParse(warnText, critText, lowerIsWorse: false, out _, out var error);
        Assert.False(ok);
        Assert.Equal("Warn must be a number", error);
    }

    [Theory]
    [InlineData("85", "xyz")]
    [InlineData("85", "")]
    public void TryParse_UnparseableCrit_FailsWithMessage(string warnText, string critText)
    {
        var ok = ThresholdInput.TryParse(warnText, critText, lowerIsWorse: false, out _, out var error);
        Assert.False(ok);
        Assert.Equal("Crit must be a number", error);
    }

    [Fact]
    public void TryParse_WarnNotBelowCrit_FailsWithMessage()
    {
        var ok = ThresholdInput.TryParse("92", "85", lowerIsWorse: false, out _, out var error);
        Assert.False(ok);
        Assert.Equal("Warn must be below crit", error);
    }

    [Fact]
    public void TryParse_LowerIsWorse_WarnNotAboveCrit_FailsWithMessage()
    {
        var ok = ThresholdInput.TryParse("30", "60", lowerIsWorse: true, out _, out var error);
        Assert.False(ok);
        Assert.Equal("Warn must be above crit when lower is worse", error);
    }

    [Fact]
    public void TryParse_EqualWarnAndCrit_Fails()
    {
        // Equal values are ordered neither way, and an active-but-equal rule would misbehave in ThresholdEvaluator
        // (which treats Warn == Crit as an inactive rule) — so this must be rejected like any other bad ordering,
        // not silently accepted as "valid but pointless".
        Assert.False(ThresholdInput.TryParse("50", "50", lowerIsWorse: false, out _, out var error));
        Assert.Equal("Warn must be below crit", error);
    }

    [Fact]
    public void TryParse_CurrentCultureFallback_ParsesCommaDecimal()
    {
        var original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            var ok = ThresholdInput.TryParse("85,5", "92,5", lowerIsWorse: false, out var rule, out var error);
            Assert.True(ok);
            Assert.Equal(85.5f, rule.Warn);
            Assert.Equal(92.5f, rule.Crit);
            Assert.Equal("", error);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = original;
        }
    }
}
