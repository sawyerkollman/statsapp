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
}
