using Stats.Core.Metrics;

namespace Stats.Core.Tests;

public class ValueFormatterTests
{
    private static MetricDefinition Def(string unit, string format = "F1") =>
        new("id", "Name", MetricGroup.Cpu, "HW", unit, format);

    [Fact]
    public void Format_Null_ReturnsDash() => Assert.Equal("—", ValueFormatter.Format(Def("°C"), null));

    [Fact]
    public void Format_NaN_ReturnsDash() => Assert.Equal("—", ValueFormatter.Format(Def("°C"), float.NaN));

    [Fact]
    public void Format_Temperature_UsesFormatAndUnit() =>
        Assert.Equal("42.5 °C", ValueFormatter.Format(Def("°C"), 42.5f));

    [Theory]
    [InlineData(512f, "512 B/s")]
    [InlineData(40_000f, "40.0 KB/s")]
    [InlineData(16_500_000f, "16.5 MB/s")]
    public void Format_Throughput_AutoScales(float value, string expected) =>
        Assert.Equal(expected, ValueFormatter.Format(Def("B/s", "F0"), value));

    // ---- FormatParts (v1.8 UI-polish §4: value/unit split, not a space-split of Format's output) ----

    [Theory]
    [InlineData(512f, "512", "B/s")]
    [InlineData(12_400f, "12.4", "KB/s")]
    [InlineData(12_400_000f, "12.4", "MB/s")]
    public void FormatParts_Throughput_AutoScalesValueAndUnit(float value, string expectedValue, string expectedUnit)
    {
        var (val, unit) = ValueFormatter.FormatParts(Def("B/s", "F0"), value);
        Assert.Equal(expectedValue, val);
        Assert.Equal(expectedUnit, unit);
    }

    [Fact]
    public void FormatParts_Temperature_SplitsValueAndUnit()
    {
        var (val, unit) = ValueFormatter.FormatParts(Def("°C"), 42.5f);
        Assert.Equal("42.5", val);
        Assert.Equal("°C", unit);
    }

    [Fact]
    public void FormatParts_UnitlessDefinition_ReturnsEmptyUnit()
    {
        var (val, unit) = ValueFormatter.FormatParts(Def(""), 3.1f);
        Assert.Equal("3.1", val);
        Assert.Equal("", unit);
    }

    [Fact]
    public void FormatParts_Null_ReturnsDashAndEmptyUnit()
    {
        var (val, unit) = ValueFormatter.FormatParts(Def("°C"), null);
        Assert.Equal("—", val);
        Assert.Equal("", unit);
    }

    [Fact]
    public void Format_ComposesFromFormatParts_SoTheyCannotDrift()
    {
        var (val, unit) = ValueFormatter.FormatParts(Def("°C"), 42.5f);
        Assert.Equal($"{val} {unit}", ValueFormatter.Format(Def("°C"), 42.5f));
    }
}
