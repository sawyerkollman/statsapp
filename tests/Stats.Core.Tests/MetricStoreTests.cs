using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class MetricStoreTests
{
    private static MetricDefinition Def(string id) =>
        new(id, id, MetricGroup.Cpu, "CPU", "°C", "F1");

    [Fact]
    public void Apply_RoutesValuesToMatchingHistories()
    {
        var store = new MetricStore(new[] { Def("a"), Def("b") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["a"] = 1f, ["b"] = 2f }, DateTime.UtcNow));
        Assert.Equal(1f, store["a"].Current);
        Assert.Equal(2f, store["b"].Current);
    }

    [Fact]
    public void Apply_MissingIdInSnapshot_SetsCurrentNull()
    {
        var store = new MetricStore(new[] { Def("a") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["a"] = 1f }, DateTime.UtcNow));
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>(), DateTime.UtcNow));
        Assert.Null(store["a"].Current);
        Assert.Equal(1f, store["a"].SessionMax); // history retained
    }

    [Fact]
    public void Apply_UnknownIdsInSnapshot_Ignored()
    {
        var store = new MetricStore(new[] { Def("a") });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["zzz"] = 9f }, DateTime.UtcNow));
        Assert.False(store.TryGet("zzz", out _));
    }
}
