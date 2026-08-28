using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class PerfCounterSensorReaderTests
{
    [Fact]
    public void Discover_CalledTwice_ReturnsSameDefinitionsWithoutDuplication()
    {
        using var reader = new PerfCounterSensorReader();

        var first = reader.Discover();
        var second = reader.Discover();

        Assert.Same(first, second);
    }

    [Fact]
    public void Discover_ThenRead_DoesNotThrow()
    {
        using var reader = new PerfCounterSensorReader();

        reader.Discover();
        var snapshot = reader.Read();

        Assert.NotNull(snapshot);
    }

    [Fact]
    public void Name_IsPerformanceCountersDegraded()
    {
        using var reader = new PerfCounterSensorReader();
        Assert.Equal("Performance Counters (degraded)", reader.Name);
        Assert.True(reader.IsDegraded);
    }

    [Fact]
    public void FanControlBackend_HasNoChannelsAndRejectsWrites()
    {
        using var reader = new PerfCounterSensorReader();
        Assert.Empty(reader.Channels);
        Assert.Throws<KeyNotFoundException>(() => reader.SetPercent("anything", 50f));
        reader.SetAuto("anything"); // no-op; must not throw
    }
}
