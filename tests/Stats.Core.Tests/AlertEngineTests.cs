using Stats.Core.Alerts;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class AlertEngineTests
{
    private static readonly DateTime T0 = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "CPU Package", MetricGroup.Cpu, "Ryzen", "°C", "F0");
    private static readonly MetricDefinition Fps = new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps", "F0");

    private static readonly ThresholdRule CritRule = new() { Group = MetricGroup.Cpu, Unit = "°C", Warn = 85, Crit = 92 };
    private static readonly ThresholdRule FpsRule = new() { Group = MetricGroup.Game, Unit = "fps", Warn = 60, Crit = 30, LowerIsWorse = true };

    private static AlertSample Crit(MetricDefinition def, float value, ThresholdRule rule) => new(def, value, Severity.Crit, rule);
    private static AlertSample Warn(MetricDefinition def) => new(def, def.Group == MetricGroup.Cpu ? 88f : 45f, Severity.Warn, CritRule);
    private static AlertSample Normal(MetricDefinition def) => new(def, 20f, Severity.Normal, CritRule);

    [Fact]
    public void EntersCrit_RaisesExactlyOnceAfterHoldSeconds_NotBefore()
    {
        var engine = new AlertEngine { HoldSeconds = 10 };
        for (int t = 0; t <= 9; t++)
            Assert.Empty(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t)));

        var events = engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(10));

        var evt = Assert.Single(events);
        Assert.Equal("cpu.temp", evt.MetricId);
        Assert.Equal("CPU Package", evt.DisplayName);
        Assert.Equal("°C", evt.Unit);
        Assert.Equal(96f, evt.PeakValue);
        Assert.Equal(92f, evt.Threshold);
        Assert.False(evt.LowerIsWorse);
    }

    [Fact]
    public void Raise_UsesInjectedLocalClock_ForRaisedAtLocal()
    {
        var localNow = new DateTime(2026, 9, 2, 8, 0, 0);
        var engine = new AlertEngine(() => localNow) { HoldSeconds = 5 };
        for (int t = 0; t <= 4; t++) engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t));

        var events = engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(5));

        Assert.Equal(localNow, Assert.Single(events).RaisedAtLocal);
    }

    [Fact]
    public void FlappingInsideHold_NeverRaises_AndEpisodeEndedReportsNoRaise()
    {
        var engine = new AlertEngine { HoldSeconds = 10 };
        (string Id, DateTime? RaisedAt, TimeSpan Duration)? ended = null;
        engine.EpisodeEnded += (id, raisedAt, duration) => ended = (id, raisedAt, duration);

        for (int t = 0; t <= 4; t++)
            Assert.Empty(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t)));
        Assert.Empty(engine.Tick(new[] { Warn(CpuTemp) }, T0.AddSeconds(5))); // leaves crit before the hold is reached

        Assert.NotNull(ended);
        Assert.Equal("cpu.temp", ended!.Value.Id);
        Assert.Null(ended.Value.RaisedAt); // nothing was ever raised — the log has no row to complete
        Assert.Equal(TimeSpan.FromSeconds(5), ended.Value.Duration);
    }

    [Fact]
    public void ReArmsOnlyAfterLeavingCrit_NotWhileStillCrit()
    {
        var engine = new AlertEngine { HoldSeconds = 5 };
        var raised = new List<AlertEvent>();
        for (int t = 0; t <= 5; t++) raised.AddRange(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t)));
        Assert.Single(raised);

        for (int t = 6; t <= 100; t++) raised.AddRange(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t)));
        Assert.Single(raised); // still crit, still disarmed: no second alert

        raised.AddRange(engine.Tick(new[] { Warn(CpuTemp) }, T0.AddSeconds(101))); // leaves crit: re-arms
        for (int t = 102; t <= 107; t++) raised.AddRange(engine.Tick(new[] { Crit(CpuTemp, 97f, CritRule) }, T0.AddSeconds(t)));
        Assert.Equal(2, raised.Count); // held the new episode long enough for a second alert
    }

    [Fact]
    public void TwoMetrics_TrackIndependently()
    {
        var engine = new AlertEngine { HoldSeconds = 5 };
        var raised = new List<AlertEvent>();
        for (int t = 0; t <= 5; t++)
            raised.AddRange(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule), Crit(Fps, 20f, FpsRule) }, T0.AddSeconds(t)));
        Assert.Equal(2, raised.Count);
        Assert.Contains(raised, e => e.MetricId == "cpu.temp");
        Assert.Contains(raised, e => e.MetricId == "fps.avg");

        raised.Clear();
        raised.AddRange(engine.Tick(new[] { Warn(CpuTemp), Crit(Fps, 20f, FpsRule) }, T0.AddSeconds(6)));
        Assert.Empty(raised); // cpu left crit (re-armed, but not re-entered); fps stays disarmed
    }

    [Fact]
    public void HoldSecondsChange_TakesEffectImmediately_ForAnInProgressEpisode()
    {
        var engine = new AlertEngine { HoldSeconds = 10 };
        for (int t = 0; t <= 2; t++) Assert.Empty(engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t)));

        engine.HoldSeconds = 3; // shortened mid-episode

        var events = engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(3));
        Assert.Single(events);
    }

    [Fact]
    public void MetricAbsentFromSamples_StateDropped_NoEpisodeEndedFired()
    {
        var engine = new AlertEngine { HoldSeconds = 100 }; // long enough that it never raises in this test
        int endedCount = 0;
        engine.EpisodeEnded += (_, _, _) => endedCount++;

        engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0); // starts an episode
        engine.Tick(Array.Empty<AlertSample>(), T0.AddSeconds(1)); // no longer monitored

        Assert.Equal(0, endedCount); // dropped silently, not "ended"

        engine.HoldSeconds = 2; // re-entering starts a brand-new episode, hold restarts from scratch
        engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(10));
        var events = engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(12));
        Assert.Single(events);
    }

    [Fact]
    public void PeakValue_TracksWorst_NormalDirection()
    {
        var engine = new AlertEngine { HoldSeconds = 3 };
        engine.Tick(new[] { Crit(CpuTemp, 93f, CritRule) }, T0);
        engine.Tick(new[] { Crit(CpuTemp, 99f, CritRule) }, T0.AddSeconds(1)); // new peak
        engine.Tick(new[] { Crit(CpuTemp, 95f, CritRule) }, T0.AddSeconds(2)); // milder — ignored

        var events = engine.Tick(new[] { Crit(CpuTemp, 94f, CritRule) }, T0.AddSeconds(3));

        Assert.Equal(99f, Assert.Single(events).PeakValue);
    }

    [Fact]
    public void PeakValue_TracksWorst_LowerIsWorseDirection()
    {
        var engine = new AlertEngine { HoldSeconds = 3 };
        engine.Tick(new[] { Crit(Fps, 25f, FpsRule) }, T0);
        engine.Tick(new[] { Crit(Fps, 10f, FpsRule) }, T0.AddSeconds(1)); // lower is worse: new peak
        engine.Tick(new[] { Crit(Fps, 20f, FpsRule) }, T0.AddSeconds(2)); // milder — ignored

        var events = engine.Tick(new[] { Crit(Fps, 15f, FpsRule) }, T0.AddSeconds(3));

        Assert.Equal(10f, Assert.Single(events).PeakValue);
    }

    [Fact]
    public void EpisodeEnded_FinalizesDuration_AfterRaising()
    {
        var engine = new AlertEngine { HoldSeconds = 5 };
        (string Id, DateTime? RaisedAt, TimeSpan Duration)? ended = null;
        engine.EpisodeEnded += (id, raisedAt, duration) => ended = (id, raisedAt, duration);

        for (int t = 0; t <= 5; t++) engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0.AddSeconds(t));
        engine.Tick(new[] { Normal(CpuTemp) }, T0.AddSeconds(20));

        Assert.NotNull(ended);
        Assert.NotNull(ended!.Value.RaisedAt);
        Assert.Equal("cpu.temp", ended.Value.Id);
        Assert.Equal(TimeSpan.FromSeconds(20), ended.Value.Duration);
    }

    [Fact]
    public void NullValue_EndsEpisode_SameAsLeavingCrit()
    {
        var engine = new AlertEngine { HoldSeconds = 3 };
        int ended = 0;
        engine.EpisodeEnded += (_, _, _) => ended++;

        engine.Tick(new[] { Crit(CpuTemp, 96f, CritRule) }, T0);
        engine.Tick(new[] { new AlertSample(CpuTemp, null, Severity.Normal, CritRule) }, T0.AddSeconds(1));

        Assert.Equal(1, ended);
    }

    [Fact]
    public void Message_NormalDirection_FormatsWithGreaterOrEqual()
    {
        var evt = new AlertEvent(T0, "cpu.temp", "CPU Package", "°C", 96f, 92f, LowerIsWorse: false);
        Assert.Equal("CPU Package 96 °C — crit ≥ 92", evt.Message);
    }

    [Fact]
    public void Message_InvertedDirection_FormatsWithLessOrEqual()
    {
        var evt = new AlertEvent(T0, "fps.avg", "FPS", "fps", 20f, 30f, LowerIsWorse: true);
        Assert.Equal("FPS 20 fps — crit ≤ 30", evt.Message);
    }
}
