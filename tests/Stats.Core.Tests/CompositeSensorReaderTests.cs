using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class CompositeSensorReaderTests
{
    private sealed class Fake : ISensorReader, IFanControlBackend
    {
        public Fake(string name, bool degraded, params (string Id, float? Value)[] values)
        { Name = name; IsDegraded = degraded; _values = values; }
        private readonly (string Id, float? Value)[] _values;
        public bool ThrowOnRead;
        public int DiscoverCalls, Disposed;
        public string Name { get; }
        public bool IsDegraded { get; }
        public List<FanChannel> FanChannels = new();
        public List<(string Id, float? Pct)> FanWrites = new();   // Pct null = SetAuto
        public IReadOnlyList<FanChannel> Channels => FanChannels;
        public void SetPercent(string channelId, float percent) => FanWrites.Add((channelId, percent));
        public void SetAuto(string channelId) => FanWrites.Add((channelId, null));
        public IReadOnlyList<MetricDefinition> Discover()
        {
            DiscoverCalls++;
            return _values.Select(v => new MetricDefinition(v.Id, v.Id, MetricGroup.Cpu, Name, "%")).ToList();
        }
        public SensorSnapshot Read()
        {
            if (ThrowOnRead) throw new InvalidOperationException("boom");
            return new SensorSnapshot(_values.ToDictionary(v => v.Id, v => v.Value), DateTime.UtcNow);
        }
        public void Dispose() => Disposed++;
    }

    [Fact]
    public void NameAndDegraded_ComeFromPrimary()
    {
        var c = new CompositeSensorReader(new Fake("LHM", true), new Fake("PM", false));
        Assert.Equal("LHM", c.Name);
        Assert.True(c.IsDegraded);
    }

    [Fact]
    public void Discover_ConcatenatesInOrder_AndCallsEachReaderOnce()
    {
        var a = new Fake("A", false, ("a1", 1), ("a2", 2));
        var b = new Fake("B", false, ("b1", 3));
        var c = new CompositeSensorReader(a, b);
        Assert.Equal(new[] { "a1", "a2", "b1" }, c.Discover().Select(d => d.Id));
        Assert.Equal(new[] { "a1", "a2", "b1" }, c.Discover().Select(d => d.Id)); // cached
        Assert.Equal(1, a.DiscoverCalls);
        Assert.Equal(1, b.DiscoverCalls);
    }

    [Fact]
    public void Read_MergesAllValues()
    {
        var c = new CompositeSensorReader(new Fake("A", false, ("a1", 1)), new Fake("B", false, ("b1", null), ("b2", 5)));
        var s = c.Read();
        Assert.Equal(1f, s.Values["a1"]);
        Assert.Null(s.Values["b1"]);
        Assert.Equal(5f, s.Values["b2"]);
    }

    [Fact]
    public void Read_OneReaderThrowing_OthersStillReported()
    {
        var a = new Fake("A", false, ("a1", 1)) { ThrowOnRead = true };
        var b = new Fake("B", false, ("b1", 2));
        var s = new CompositeSensorReader(a, b).Read();
        Assert.False(s.Values.ContainsKey("a1"));
        Assert.Equal(2f, s.Values["b1"]);
    }

    [Fact]
    public void Dispose_DisposesEveryReader()
    {
        var a = new Fake("A", false); var b = new Fake("B", false);
        new CompositeSensorReader(a, b).Dispose();
        Assert.Equal(1, a.Disposed);
        Assert.Equal(1, b.Disposed);
    }

    [Fact]
    public void FanBackend_ForwardsToFirstReaderWithChannels()
    {
        var a = new Fake("A", false);
        var b = new Fake("B", false) { FanChannels = { new FanChannel("/x/control/0", "Fan #1", "ITE", null, null, 0, 100) } };
        var c = new CompositeSensorReader(a, b);
        Assert.Single(c.Channels);
        c.SetPercent("/x/control/0", 40);
        c.SetAuto("/x/control/0");
        Assert.Equal(new (string, float?)[] { ("/x/control/0", 40f), ("/x/control/0", null) }, b.FanWrites);
        Assert.Empty(a.FanWrites);
    }

    [Fact]
    public void FanBackend_NoReaderHasChannels_EmptyAndWritesAreNoOps()
    {
        var c = new CompositeSensorReader(new Fake("A", false));
        Assert.Empty(c.Channels);
        c.SetPercent("nope", 50); // must not throw
        c.SetAuto("nope");
    }
}
