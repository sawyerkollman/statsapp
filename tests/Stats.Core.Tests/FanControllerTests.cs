using Stats.Core.Fans;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.Core.Tests;

public class FanControllerTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new();
        public List<(string Id, float? Pct)> Writes = new();
        public Func<string, bool>? FailWrite;
        public Func<string, bool>? FailAuto;
        public bool RecordBeforeThrow; // models a partial write: the hardware saw it, then the call still threw
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p)
        {
            bool fail = FailWrite?.Invoke(id) == true;
            if (fail && RecordBeforeThrow) Writes.Add((id, p));
            if (fail) throw new InvalidOperationException("io");
            Writes.Add((id, p));
        }
        public void SetAuto(string id)
        {
            if (FailAuto?.Invoke(id) == true) throw new InvalidOperationException("io-auto");
            Writes.Add((id, null));
        }
    }

    private const string Case = "/lpc/it8696e/0/control/0";
    private const string Gpu = "/gpu-nvidia/0/control/1";
    private const string Pump = "/usbhid/0/fan/14";
    private const string Cpu = "cpu.amd.temperature.tctl";
    private const string CaseRpm = "motherboard.ite.fan.fan-1";
    private static readonly DateTime T0 = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    private sealed class H
    {
        public FakeBackend B = new();
        public AppSettings S = new() { FanControlEnabled = true };
        public int Saves;
        public FanController C;
        public H()
        {
            B.Chans.Add(new FanChannel(Case, "Fan #1", "ITE IT8696E", CaseRpm, null, 0, 100));
            B.Chans.Add(new FanChannel(Gpu, "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
            B.Chans.Add(new FanChannel(Pump, "Pump", "MSI CoreLiquid S360", null, null, 0, 100));
            C = new FanController(B, S, () => Saves++);
        }
        public SensorSnapshot Snap(float? cpu, float? rpm = 1200) => new(new Dictionary<string, float?> { [Cpu] = cpu, [CaseRpm] = rpm }, T0);
        public void Tick(float? cpu, int secondsFromT0 = 0) => C.Tick(Snap(cpu), T0.AddSeconds(secondsFromT0));
        public IEnumerable<(string, float?)> WritesFor(string id) => B.Writes.Where(w => w.Id == id).Select(w => (w.Id, w.Pct));
    }

    // 1 %/°C from (30,0) to (90,60): easy arithmetic.
    private static readonly FanPoint[] Linear = { new(30, 0), new(90, 60) };

    [Fact]
    public void Disabled_NeverWrites_AndRestoresWhatWasInSoftware()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));
        h.C.Enabled = false;
        Assert.False(h.S.FanControlEnabled);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
        h.Tick(50);
        Assert.Equal(2, h.WritesFor(Case).Count()); // nothing more while disabled
    }

    [Fact]
    public void Auto_NoWrites_WhenNeverInSoftware()
    {
        var h = new H();
        h.Tick(50); h.Tick(60);
        Assert.Empty(h.B.Writes);
    }

    [Fact]
    public void Manual_WritesOnce_ThenOnlyOnChange()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));
        h.C.SetManualPercent(Case, 45);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, 45f) }, h.WritesFor(Case));
    }

    [Fact]
    public void Curve_FollowsSource_WithTwoDegreeHysteresis()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); Assert.True(h.C.TrySetPoints(Case, Linear));
        h.Tick(50);            // 20 %
        h.Tick(51.9f);         // < 2 °C change → no write
        Assert.Equal(new (string, float?)[] { (Case, 20f) }, h.WritesFor(Case));
        h.Tick(52f);           // 2 °C → 22 %
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, 22f) }, h.WritesFor(Case));
        h.Tick(50.1f);         // 1.9 below 52 → no write
        Assert.Equal(2, h.WritesFor(Case).Count());
    }

    [Fact]
    public void SlewLimit_TenPointsPerTick()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 0);
        h.Tick(50);                                   // first write immediate: 0
        h.C.SetManualPercent(Case, 100);
        for (int i = 0; i < 12; i++) h.Tick(50);
        var w = h.WritesFor(Case).Select(x => x.Item2).ToList();
        Assert.Equal(new float?[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }, w);
    }

    [Fact]
    public void Curve_NoSourceValueYet_WaitsThenFailsSafeToAutoAfterTenSeconds()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(null, 0); h.Tick(null, 5);
        Assert.Empty(h.B.Writes);
        Assert.Equal(FanChannelStatus.WaitingForSource, h.C.Views().Single(v => v.Id == Case).Status);
        h.Tick(null, 11);
        Assert.Empty(h.B.Writes);                     // never in software → nothing to restore
        Assert.Equal(FanChannelStatus.SourceUnavailable, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void Curve_SourceGoesStale_RevertsToAuto_ThenRecovers()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(50, 0);                                // 20 %
        h.Tick(null, 5);                              // stale < 10 s: hold
        h.Tick(null, 9);
        Assert.Equal(new (string, float?)[] { (Case, 20f) }, h.WritesFor(Case));
        h.Tick(null, 11);                             // > 10 s → SetAuto
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, null) }, h.WritesFor(Case));
        Assert.Equal(FanChannelStatus.SourceUnavailable, h.C.Views().Single(v => v.Id == Case).Status);
        h.Tick(70, 12);                               // source back → 40 %, immediate (lastWritten cleared on SetAuto)
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
        Assert.Equal(FanChannelStatus.Active, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void Floors_GpuMin30_PumpMin50()
    {
        var h = new H();
        h.C.SetMode(Gpu, FanMode.Manual); h.C.SetManualPercent(Gpu, 10);
        h.C.SetMode(Pump, FanMode.Manual); h.C.SetManualPercent(Pump, 10);
        h.Tick(50);
        Assert.Equal((Gpu, 30f), h.WritesFor(Gpu).Single());
        Assert.Equal((Pump, 50f), h.WritesFor(Pump).Single());
    }

    [Fact]
    public void ModeChange_CurveToAuto_EmitsSetAutoOnce()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.Tick(50);
        h.C.SetMode(Case, FanMode.Auto);
        h.Tick(50); h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 20f), (Case, null) }, h.WritesFor(Case));
    }

    [Fact]
    public void WriteFailures_ThreeInARow_ChannelGoesAuto()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);
        h.Tick(50);
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status); // status kept for the user
        h.Tick(50);
        // The 3rd failing write still marked InSoftware before the throw, so the fail-safe flip released the
        // channel via a (successful) SetAuto — one (Case, null) entry. The 4th tick (now Auto, already released)
        // writes nothing further.
        Assert.Equal(new (string, float?)[] { (Case, null) }, h.WritesFor(Case));
    }

    [Fact]
    public void FailedSetAuto_KeepsChannelTracked_AndRetriesNextTick()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));

        h.B.FailAuto = id => id == Case;
        h.C.SetMode(Case, FanMode.Auto);
        h.Tick(50); // SetAuto attempted, throws — channel stays tracked in software
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case)); // no new write recorded
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);

        h.B.FailAuto = null;
        h.Tick(50); // retried automatically — SetAuto now succeeds
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void RestoreAll_RetriesAfterFailedRelease()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);

        h.B.FailAuto = id => id == Case;
        h.C.RestoreAll(); // SetAuto throws — channel still tracked as in-software
        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));

        h.B.FailAuto = null;
        h.C.RestoreAll(); // retried — succeeds this time
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
    }

    [Fact]
    public void PartialWriteFailure_StillReleasedAfterThreeFailures()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.B.RecordBeforeThrow = true; // the hardware took the value, then the call still threw
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);
        // Each failing write is recorded (partial write) then throws; on the 3rd failure the channel is marked
        // InSoftware before the throw, so the fail-safe flip can actually release it via SetAuto.
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, 40f), (Case, 40f), (Case, null) }, h.WritesFor(Case));
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
    }

    [Fact]
    public void RestoreAll_OnlyTouchedChannels()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        h.C.RestoreAll();
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null) }, h.WritesFor(Case));
        Assert.Empty(h.WritesFor(Gpu));
        h.Tick(50); // next tick after restore re-applies Manual (still enabled)
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
    }

    [Fact]
    public void Views_ReflectRpmPercentTargetAndSettings()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear); h.C.SetName(Case, "Front");
        h.Tick(50);
        var v = h.C.Views().Single(x => x.Id == Case);
        Assert.Equal("Front", v.Name);
        Assert.Equal(1200f, v.Rpm);
        Assert.Equal(20f, v.TargetPercent);
        Assert.Equal(20f, v.Percent);           // no PercentMetricId → last written
        Assert.Equal(50f, v.SourceTemp);
        Assert.Equal(FanMode.Curve, v.Mode);
        Assert.Equal(Cpu, v.SourceMetricId);
        Assert.Equal(Linear, v.Points);
        var g = h.C.Views().Single(x => x.Id == Gpu);
        Assert.Equal("GPU Fan 1", g.Name);       // no pref → hardware name, Auto, Idle
        Assert.Equal(FanMode.Auto, g.Mode);
        Assert.Equal(FanChannelStatus.Idle, g.Status);
    }

    [Fact]
    public void Setters_PersistAndSave_InvalidPointsRejected()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual);
        h.C.SetManualPercent(Case, 33);
        h.C.SetSource(Case, Cpu);
        Assert.False(h.C.TrySetPoints(Case, new[] { new FanPoint(50, 50) }));
        Assert.True(h.C.TrySetPoints(Case, Linear));
        h.C.ResetCurve(Case);
        var p = h.S.FanChannels[Case];
        Assert.Equal(FanMode.Manual, p.Mode);
        Assert.Equal(33f, p.ManualPercent);
        Assert.Equal(Cpu, p.SourceMetricId);
        Assert.Equal(FanCurve.DefaultPoints, p.Points);
        Assert.Equal(5, h.Saves); // SetMode, SetManualPercent, SetSource, TrySetPoints(valid), ResetCurve
    }
}
