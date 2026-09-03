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
        /// <summary>Test hook invoked after a successful SetPercent call actually lands (i.e. from phase 2, on
        /// the poller thread, with the controller's gate released) — id and the percent just written.</summary>
        public Action<string, float>? OnSetPercent;
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p)
        {
            bool fail = FailWrite?.Invoke(id) == true;
            if (fail && RecordBeforeThrow) Writes.Add((id, p));
            if (fail) { OnSetPercent?.Invoke(id, p); throw new InvalidOperationException("io"); }
            Writes.Add((id, p));
            OnSetPercent?.Invoke(id, p);
        }
        public void SetAuto(string id)
        {
            if (FailAuto?.Invoke(id) == true) throw new InvalidOperationException("io-auto");
            Writes.Add((id, null));
        }
    }

    private sealed class FakeMarker : IFanArmedMarker
    {
        public bool Present; public int Sets, Clears;
        public bool FailNextSet; // models a transient marker write failure (AV lock, disk full)
        public bool Exists() => Present;
        public bool Set() { Sets++; if (FailNextSet) { FailNextSet = false; return false; } Present = true; return true; }
        public void Clear() { Clears++; Present = false; }
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
        public FakeMarker M = new();
        public int Saves;
        public FanController C;
        public H()
        {
            B.Chans.Add(new FanChannel(Case, "Fan #1", "ITE IT8696E", CaseRpm, null, 0, 100));
            B.Chans.Add(new FanChannel(Gpu, "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
            B.Chans.Add(new FanChannel(Pump, "Pump", "MSI CoreLiquid S360", null, null, 0, 100));
            C = new FanController(B, S, () => Saves++, M);
        }
        public SensorSnapshot Snap(float? cpu, float? rpm = 1200) => new(new Dictionary<string, float?> { [Cpu] = cpu, [CaseRpm] = rpm }, T0);
        public SensorSnapshot Snap2(float? cpu, float? gpu) => new(new Dictionary<string, float?> { [Cpu] = cpu, ["gpu.core"] = gpu, [CaseRpm] = 1200 }, T0);
        public SensorSnapshot FpsSnap(float fps) => new(new Dictionary<string, float?> { [Cpu] = 50f, [CaseRpm] = 1200, [GameModeSwitcher.FpsMetricId] = fps }, T0);
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
    public void WriteFailed_StatusStaysUntilUserChangesMode()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);                      // 3rd failure flips the channel to Auto
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
        h.Tick(50); h.Tick(50);                                  // Auto branch keeps reporting the failure
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);

        h.B.FailWrite = null;
        h.C.SetMode(Case, FanMode.Manual);                       // the user acted: fresh start
        h.Tick(50);
        Assert.Equal(FanChannelStatus.Active, h.C.Views().Single(v => v.Id == Case).Status);
    }

    [Fact]
    public void BadRange_MaxZero_NeverWritesZero_NoThrow()
    {
        var h = new H();
        const string Dead = "/lpc/it8696e/0/control/9";
        h.B.Chans.Add(new FanChannel(Dead, "Fan #9", "ITE IT8696E", null, null, 0, 0));
        h.C.SetMode(Dead, FanMode.Manual); h.C.SetManualPercent(Dead, 40);
        h.Tick(50);                                              // must not throw
        Assert.Empty(h.WritesFor(Dead));                         // never driven to a hard 0 %
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Dead).Status);
    }

    [Fact]
    public void PumpFloorAboveMax_DoesNotThrow_ClampsToMax()
    {
        var h = new H();
        const string Capped = "/usbhid/0/fan/15";
        h.B.Chans.Add(new FanChannel(Capped, "Pump", "MSI CoreLiquid S360", null, null, 0, 10));
        h.C.SetMode(Capped, FanMode.Manual); h.C.SetManualPercent(Capped, 70);
        h.Tick(50);                                              // 50 % pump floor > 10 % ceiling
        Assert.Equal((Capped, 10f), h.WritesFor(Capped).Single());
    }

    [Fact]
    public void MasterSwitch_OffThenOn_ReappliesImmediately_NoSlew()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 80);
        h.Tick(50);
        Assert.Equal(new (string, float?)[] { (Case, 80f) }, h.WritesFor(Case));
        h.C.Enabled = false;
        h.Tick(50);                                              // released
        Assert.Equal(new (string, float?)[] { (Case, 80f), (Case, null) }, h.WritesFor(Case));
        h.C.Enabled = true;
        h.Tick(50);                                              // straight back to 80, not slewed from 0
        Assert.Equal(new (string, float?)[] { (Case, 80f), (Case, null), (Case, 80f) }, h.WritesFor(Case));
    }

    [Fact]
    public void PumpFloor_MatchesNameVariants()
    {
        var h = new H();
        var ids = new[] { "/c/0", "/c/1", "/c/2", "/c/3" };
        var names = new[] { "Water Pump", "PUMP FAN", "pump", "Radiator Fan" };
        for (int i = 0; i < ids.Length; i++)
        {
            h.B.Chans.Add(new FanChannel(ids[i], names[i], "AIO", null, null, 0, 100));
            h.C.SetMode(ids[i], FanMode.Manual); h.C.SetManualPercent(ids[i], 10);
        }
        h.Tick(50);
        Assert.Equal((ids[0], 50f), h.WritesFor(ids[0]).Single());
        Assert.Equal((ids[1], 50f), h.WritesFor(ids[1]).Single());
        Assert.Equal((ids[2], 50f), h.WritesFor(ids[2]).Single());
        Assert.Equal((ids[3], 10f), h.WritesFor(ids[3]).Single());
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

    [Fact]
    public void Curve_MultiSource_UsesMaxOfPresentValues()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSources(Case, new[] { Cpu, "gpu.core" }); h.C.TrySetPoints(Case, Linear);
        h.C.Tick(h.Snap2(50, 70), T0);            // max 70 → 40 %
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
        h.C.Tick(h.Snap2(null, 80), T0.AddSeconds(1)); // cpu null → gpu 80 → 50 % (slew allows +10)
        Assert.Equal((Case, 50f), h.WritesFor(Case).Last());
        Assert.Equal(80f, h.C.Views().Single(v => v.Id == Case).SourceTemp);
        Assert.Equal(new[] { Cpu, "gpu.core" }, h.C.Views().Single(v => v.Id == Case).SourceMetricIds);
    }

    [Fact]
    public void SetSources_Dedupes_DropsBlanks_KeepsFirstInLegacyField()
    {
        var h = new H();
        h.C.SetSources(Case, new[] { " ", Cpu, Cpu, "gpu.core" });
        var p = h.S.FanChannels[Case];
        Assert.Equal(new[] { Cpu, "gpu.core" }, p.SourceMetricIds);
        Assert.Equal(Cpu, p.SourceMetricId);
        h.C.SetSources(Case, Array.Empty<string>());
        Assert.Empty(h.S.FanChannels[Case].SourceMetricIds);
        Assert.Null(h.S.FanChannels[Case].SourceMetricId);
    }

    [Fact]
    public void Marker_SetOnFirstSoftwareWrite_ClearedWhenAllReleased()
    {
        var h = new H();
        h.Tick(50); Assert.Equal(0, h.M.Sets);
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40); h.Tick(50);
        Assert.Equal(1, h.M.Sets); Assert.True(h.M.Present);
        h.Tick(50); Assert.Equal(1, h.M.Sets);            // not rewritten every tick
        h.C.RestoreAll();
        Assert.False(h.M.Present); Assert.Equal(1, h.M.Clears);
    }

    [Fact]
    public void Marker_KeptWhenReleaseFails()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40); h.Tick(50);
        h.B.FailAuto = id => id == Case;
        h.C.RestoreAll();
        Assert.True(h.M.Present);
    }

    [Fact]
    public void Recover_ReleasesEveryBackendChannel_AndClears()
    {
        var h = new H(); h.M.Present = true;
        Assert.True(h.C.RecoverFromUncleanShutdown());
        Assert.Equal(3, h.B.Writes.Count(w => w.Pct is null)); // Case, Gpu, Pump
        Assert.False(h.M.Present);
        Assert.False(h.C.RecoverFromUncleanShutdown()); // no marker now
    }

    [Fact]
    public void Recover_WithEmptyBackendChannels_KeepsMarker_AndReportsFalse()
    {
        // Degraded-launch case: LHM failed to open (or exposes no fan channels) and the app fell back to
        // perf counters, so there is nothing to release. Discarding the marker here would falsely claim
        // "fans returned to device control" while the crashed PWM stays pinned until a healthy launch.
        var h = new H(); h.M.Present = true;
        h.B.Chans.Clear();
        Assert.False(h.C.RecoverFromUncleanShutdown());
        Assert.Empty(h.B.Writes);
        Assert.True(h.M.Present);
        Assert.Equal(0, h.M.Clears);
    }

    [Fact]
    public void SnapshotProfile_IsDeepCopy()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.TrySetPoints(Case, Linear);
        var prof = h.C.SnapshotProfile("Mine");
        prof.Channels[Case].Points[0] = new FanPoint(99, 99);
        prof.Channels[Case].Mode = FanMode.Manual;
        Assert.Equal(FanMode.Curve, h.S.FanChannels[Case].Mode);
        Assert.Equal(30f, h.S.FanChannels[Case].Points[0].TempC);
    }

    [Fact]
    public void ApplyProfile_ReplacesChannels_MissingBecomeAuto_SetsActive_ClearedOnEdit()
    {
        var h = new H();
        h.C.SetMode(Gpu, FanMode.Manual);
        var prof = new FanProfile { Name = "P", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 70 } } };
        h.C.ApplyProfile(prof);
        Assert.Equal("P", h.S.ActiveFanProfile);
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Gpu].Mode);
        prof.Channels[Case].ManualPercent = 10; // applied copy is independent
        Assert.Equal(70f, h.S.FanChannels[Case].ManualPercent);
        h.Tick(50);
        Assert.Equal((Case, 70f), h.WritesFor(Case).Last());
        h.C.SetManualPercent(Case, 60);
        Assert.Null(h.S.ActiveFanProfile);
    }

    [Fact]
    public void ApplyProfile_DeferSave_SavesAtEndOfNextTick()
    {
        var h = new H();
        int before = h.Saves;
        h.C.ApplyProfile(new FanProfile { Name = "P" }, deferSave: true);
        Assert.Equal(before, h.Saves);
        h.Tick(50);
        Assert.Equal(before + 1, h.Saves);
    }

    [Fact]
    public void CreateDefaultProfiles_ThreeProfiles_SourcesByDevice_PumpsAuto()
    {
        var h = new H();
        var profs = FanController.CreateDefaultProfiles(h.B.Chans, "cpu.t", "gpu.t");
        Assert.Equal(new[] { "Silent", "Balanced", "Gaming" }, profs.Select(p => p.Name));
        var gaming = profs[2];
        Assert.Equal(FanMode.Curve, gaming.Channels[Case].Mode);
        Assert.Equal(new[] { "cpu.t" }, gaming.Channels[Case].SourceMetricIds);
        Assert.Equal(new[] { "gpu.t" }, gaming.Channels[Gpu].SourceMetricIds);   // Device "RTX 5070 Ti"
        Assert.Equal(FanMode.Auto, gaming.Channels[Pump].Mode);
        Assert.Equal(new FanPoint(30, 40), gaming.Channels[Case].Points[0]);
        Assert.Equal(FanCurve.DefaultPoints, profs[1].Channels[Case].Points);
        Assert.Equal(new FanPoint(30, 20), profs[0].Channels[Case].Points[0]);
        var none = FanController.CreateDefaultProfiles(h.B.Chans, null, null);
        Assert.Equal(FanMode.Auto, none[2].Channels[Case].Mode); // no source → Auto
    }

    [Fact]
    public void Recover_PartialFailure_KeepsMarkerAndTracking_ReleasedByLaterRestoreAll()
    {
        var h = new H(); h.M.Present = true;
        h.B.FailAuto = id => id == Gpu;
        Assert.True(h.C.RecoverFromUncleanShutdown(out var partial));
        Assert.True(partial);
        Assert.True(h.M.Present);                    // still driven somewhere → the marker must survive
        Assert.Equal(0, h.M.Clears);

        h.B.FailAuto = null;
        h.C.RestoreAll();                            // the channel stayed tracked, so the release is retried
        Assert.Contains(h.WritesFor(Gpu), w => w.Item2 is null);
        Assert.False(h.M.Present);
    }

    [Fact]
    public void Recover_AllReleased_ReportsNotPartial()
    {
        var h = new H(); h.M.Present = true;
        Assert.True(h.C.RecoverFromUncleanShutdown(out var partial));
        Assert.False(partial);
        Assert.False(h.M.Present);
    }

    [Fact]
    public void Marker_NotSetWhileFanControlDisabled()
    {
        var h = new H();
        h.S.FanControlEnabled = false;
        h.C.SetMode(Case, FanMode.Manual);
        h.Tick(50);
        Assert.Equal(0, h.M.Sets);       // nothing was driven, so the next launch must not claim an unclean exit
        Assert.False(h.M.Present);
    }

    [Fact]
    public void Marker_FailedSet_IsRetriedOnTheNextWrite()
    {
        var h = new H();
        h.M.FailNextSet = true;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50);
        Assert.False(h.M.Present);       // one transient failure …
        h.C.SetManualPercent(Case, 50);
        h.Tick(50);
        Assert.True(h.M.Present);        // … must not disable crash recovery for the whole session
        Assert.Equal(2, h.M.Sets);
    }

    [Fact]
    public void ApplyProfile_ChannelMissingFromProfile_IsHandedBackToTheDevice()
    {
        var h = new H();
        h.C.SetMode(Gpu, FanMode.Manual); h.C.SetManualPercent(Gpu, 60);
        h.Tick(50);
        Assert.Contains(h.WritesFor(Gpu), w => w.Item2 == 60f);   // actually driven before the switch
        h.C.ApplyProfile(new FanProfile { Name = "P", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 40 } } });
        h.Tick(50);
        Assert.Contains(h.WritesFor(Gpu), w => w.Item2 is null);  // SetAuto reached the backend, not just the pref
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Gpu).Status);
    }

    [Fact]
    public void ApplyProfile_ResetsRuntime_CurveFollowsTheNewSourceImmediately()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Curve); h.C.SetSource(Case, Cpu); h.C.TrySetPoints(Case, Linear);
        h.C.Tick(h.Snap2(50, 51), T0);
        Assert.Equal((Case, 20f), h.WritesFor(Case).Last());
        h.C.ApplyProfile(new FanProfile
        {
            Name = "P",
            Channels = { [Case] = new FanChannelPref { Mode = FanMode.Curve, SourceMetricIds = { "gpu.core" }, SourceMetricId = "gpu.core", Points = Linear.ToList() } },
        });
        h.C.Tick(h.Snap2(50, 51), T0.AddSeconds(1));
        // 51 °C is within the 2 °C hysteresis band of the 50 °C the old source last used: only a reset
        // LastSourceUsed lets the new source take effect on the very first tick.
        Assert.Equal((Case, 21f), h.WritesFor(Case).Last());
    }

    [Fact]
    public void AutomaticProfileSwitch_KeepsTheWriteFailSafe_OneProbeWritePerTransition()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.B.RecordBeforeThrow = true;              // count attempts, not successes
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);        // three failures → parked in Auto, status kept
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);
        int attempts = h.WritesFor(Case).Count(w => w.Item2 is not null);

        h.S.GameModeEnabled = true;
        h.S.GameModeGamingProfile = "Gaming";
        h.C.AddOrReplaceProfile(new FanProfile { Name = "Gaming", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 80 } } });
        var sw = new GameModeSwitcher(h.C, h.S);
        for (int t = 0; t <= 5; t++) sw.Tick(h.FpsSnap(120), T0.AddSeconds(t));
        Assert.True(sw.IsGaming);
        h.C.Tick(h.FpsSnap(120), T0.AddSeconds(6));

        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);                                 // still parked
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status); // status survives
        Assert.Equal(attempts + 1, h.WritesFor(Case).Count(w => w.Item2 is not null));           // one probe, not three
    }

    [Fact]
    public void UserProfileLoad_ResetsTheFailureBudget_UnlikeAnAutomaticSwitch()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.B.RecordBeforeThrow = true;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);
        int attempts = h.WritesFor(Case).Count(w => w.Item2 is not null);

        h.C.ApplyProfile(new FanProfile { Name = "P", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 80 } } });
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);
        h.Tick(50); h.Tick(50); h.Tick(50);
        Assert.Equal(attempts + 3, h.WritesFor(Case).Count(w => w.Item2 is not null)); // the user acted: full budget
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
    }

    [Fact]
    public void PreservedFailures_StillRecover_WhenAWriteFinallySucceeds()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); h.Tick(50);
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);

        h.B.FailWrite = null;
        h.C.ApplyProfile(new FanProfile { Name = "Desktop", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 30 } } }, resetFailures: false);
        h.Tick(50);
        Assert.Equal(FanChannelStatus.Active, h.C.Views().Single(v => v.Id == Case).Status);
        Assert.Equal((Case, 30f), h.WritesFor(Case).Last());
    }

    [Fact]
    public void Profiles_AddReplaceRemoveAndLookup_GoThroughTheGate()
    {
        var h = new H();
        h.C.AddOrReplaceProfile(new FanProfile { Name = "A", Channels = { [Case] = new FanChannelPref { ManualPercent = 10 } } });
        h.C.AddOrReplaceProfile(new FanProfile { Name = "A", Channels = { [Case] = new FanChannelPref { ManualPercent = 20 } } });
        Assert.Equal(new[] { "A" }, h.C.ProfileNames());
        Assert.True(h.C.TryGetProfile("A", out var a));
        Assert.Equal(20f, a!.Channels[Case].ManualPercent);
        Assert.False(h.C.TryGetProfile("B", out var b));
        Assert.Null(b);
        var added = h.C.AddProfilesIfMissing(new[] { new FanProfile { Name = "A" }, new FanProfile { Name = "B" } });
        Assert.Equal(new[] { "B" }, added.Select(p => p.Name));
        Assert.Equal(new[] { "A", "B" }, h.C.ProfileNames());
        Assert.True(h.C.RemoveProfile("A"));
        Assert.False(h.C.RemoveProfile("A"));
        Assert.Equal(new[] { "B" }, h.C.ProfileNames());
    }

    // ---- Identify ----

    [Fact]
    public void Identify_PulseWritesMaxPercent_OnNextTick()
    {
        var h = new H();
        h.C.Identify(Gpu, T0);                     // Auto channel, never touched before
        h.C.Tick(h.Snap(50), T0);
        Assert.Equal((Gpu, 100f), h.WritesFor(Gpu).Single());
    }

    [Fact]
    public void Identify_Expires_AutoChannelIsReleased()
    {
        var h = new H();
        h.C.Identify(Gpu, T0);
        h.C.Tick(h.Snap(50), T0);
        Assert.Equal((Gpu, 100f), h.WritesFor(Gpu).Single());
        h.C.Tick(h.Snap(50), T0.AddSeconds(2.1));  // pulse expired: Auto's own mode releases it
        Assert.Equal(new (string, float?)[] { (Gpu, 100f), (Gpu, null) }, h.WritesFor(Gpu));
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Gpu).Status);
    }

    [Fact]
    public void Identify_Expires_ManualChannelRampsBackToItsOwnTarget()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.C.Tick(h.Snap(50), T0);                  // establishes Manual at 40 first
        Assert.Equal((Case, 40f), h.WritesFor(Case).Last());
        h.C.Identify(Case, T0.AddSeconds(1));
        h.C.Tick(h.Snap(50), T0.AddSeconds(1));    // pulse: straight to max, no slew
        Assert.Equal((Case, 100f), h.WritesFor(Case).Last());
        h.C.Tick(h.Snap(50), T0.AddSeconds(3.1));  // expired: Manual resumes, ramping down at the normal slew rate
        Assert.Equal((Case, 90f), h.WritesFor(Case).Last());
        h.C.Tick(h.Snap(50), T0.AddSeconds(4));
        Assert.Equal((Case, 80f), h.WritesFor(Case).Last());
    }

    [Fact]
    public void Identify_NeverTouchesPrefsOrActiveProfile()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.C.ApplyProfile(new FanProfile { Name = "P", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 40 } } });
        Assert.Equal("P", h.S.ActiveFanProfile);
        h.C.Identify(Case, T0);
        h.C.Tick(h.Snap(50), T0);
        Assert.Equal("P", h.S.ActiveFanProfile);                 // Identify is not a "user edit" of the pref
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);
        Assert.Equal(40f, h.S.FanChannels[Case].ManualPercent);
    }

    [Fact]
    public void Identify_WhileFanControlDisabled_DoesNothing()
    {
        var h = new H();
        h.C.Enabled = false;
        h.C.Identify(Gpu, T0);
        h.C.Tick(h.Snap(50), T0);
        Assert.Empty(h.B.Writes);
    }

    [Fact]
    public void Identify_DuringMidFailureCount_CountsTowardTheSameFailSafe()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.C.Tick(h.Snap(50), T0); h.C.Tick(h.Snap(50), T0.AddSeconds(1)); // two failures already on the shared counter
        Assert.Equal(FanChannelStatus.WriteFailed, h.C.Views().Single(v => v.Id == Case).Status);
        Assert.Equal(FanMode.Manual, h.S.FanChannels[Case].Mode);

        h.C.Identify(Case, T0.AddSeconds(2));
        h.C.Tick(h.Snap(50), T0.AddSeconds(2));    // the identify pulse's write is the 3rd failure → fail-safe trips
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
    }

    [Fact]
    public void PollThreadProfileSwitches_AndUiEdits_NeverSerializeAMutatingCollection()
    {
        var b = new FakeBackend();
        b.Chans.Add(new FanChannel(Case, "Fan #1", "ITE IT8696E", CaseRpm, null, 0, 100));
        b.Chans.Add(new FanChannel(Gpu, "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
        var s = new AppSettings { FanControlEnabled = true };
        s.FanProfiles.Add(new FanProfile { Name = "Gaming", Channels = { [Case] = new FanChannelPref { Mode = FanMode.Manual, ManualPercent = 80 } } });
        Exception? failure = null;
        // Every real save serializes the whole graph under AppSettings.SyncRoot (SettingsService.Serialize's
        // contract, which App.SaveSettings implements). The controller's gate IS that lock, so a poll-thread
        // profile switch can never be mid-Clear()+insert while the serializer walks FanChannels.
        var c = new FanController(b, s, () =>
        {
            try { lock (s.SyncRoot) SettingsService.Serialize(s); }
            catch (Exception ex) { failure ??= ex; }
        });
        var prof = s.FanProfiles[0];
        var snap = new SensorSnapshot(new Dictionary<string, float?> { [Cpu] = 50f, [CaseRpm] = 1200f }, T0);

        var poll = new Thread(() =>
        {
            try
            {
                for (int i = 0; i < 400; i++)
                {
                    c.ApplyProfile(prof, deferSave: true, resetFailures: false);
                    c.Tick(snap, T0.AddSeconds(i));
                }
            }
            catch (Exception ex) { failure ??= ex; }
        });
        poll.Start();
        for (int i = 0; i < 400; i++) { c.SetManualPercent(Gpu, 40 + (i % 20)); c.SetName(Case, "Fan " + i); }
        poll.Join();

        Assert.Null(failure);
    }

    // ---- Fan lock scope: backend I/O runs outside _gate (Tick's phase 2) ----

    [Fact]
    public void BackendWrite_RunsWithGateReleased_AnotherThreadCanAcquireItMidCall()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        var acquired = new ManualResetEventSlim(false);
        bool gotLock = false;
        h.B.OnSetPercent = (_, _) =>
        {
            var t = new Thread(() =>
            {
                // If Tick still held _gate while calling into the backend, this — a different thread, so the
                // reentrant Monitor gives it no free pass — would block until the lock is released.
                gotLock = Monitor.TryEnter(h.S.SyncRoot, TimeSpan.FromSeconds(3));
                if (gotLock) Monitor.Exit(h.S.SyncRoot);
                acquired.Set();
            });
            t.IsBackground = true;
            t.Start();
            Assert.True(acquired.Wait(TimeSpan.FromSeconds(5)), "a concurrent thread never got a chance to acquire the gate");
        };

        h.Tick(50);

        Assert.True(gotLock, "SetPercent ran while _gate was held, which would stall a concurrent UI-thread setter/save");
        Assert.Equal((Case, 40f), h.WritesFor(Case).Single());
    }

    [Fact]
    public void UiMutation_FromInsideBackendCall_DoesNotDeadlock()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        // Models a UI-thread setter racing the poller's backend call. If Tick held _gate across SetPercent, this
        // (a different thread taking the same lock) would hang until the test's own timeout killed the run.
        h.B.OnSetPercent = (_, _) =>
        {
            var t = new Thread(() => h.C.SetManualPercent(Gpu, 77));
            t.IsBackground = true;
            t.Start();
            Assert.True(t.Join(TimeSpan.FromSeconds(5)), "UI-thread mutation from inside the backend call deadlocked");
        };

        h.Tick(50);

        Assert.Equal(77f, h.S.FanChannels[Gpu].ManualPercent);
    }

    [Fact]
    public void Marker_LatchedBeforeTheFirstBackendCall()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        bool? markerPresentDuringCall = null;
        h.B.OnSetPercent = (_, _) => markerPresentDuringCall ??= h.M.Present;

        h.Tick(50);

        Assert.True(markerPresentDuringCall, "InSoftware/the armed marker must be latched in phase 1, before phase 2's backend call");
    }

    [Fact]
    public void PrefMutatedDuringBackendCall_OutcomeStillAppliesToTheChannelActuallyWritten()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        // "The UI" changes the channel's mode mid-write (between phase 1's decision and phase 3's bookkeeping).
        // Phase 3 must still apply the write's outcome via the Runtime object captured in phase 1, not by
        // re-deriving anything from the pref's current (now different) mode.
        h.B.OnSetPercent = (_, _) => h.C.SetMode(Case, FanMode.Curve);

        h.Tick(50);

        Assert.Equal((Case, 40f), h.WritesFor(Case).Single());
        var v = h.C.Views().Single(x => x.Id == Case);
        Assert.Equal(FanChannelStatus.Active, v.Status);   // the write succeeded — not overwritten by the mode change
        Assert.Equal(40f, v.Percent);                      // LastWritten applied to the right Runtime
        Assert.Equal(FanMode.Curve, h.S.FanChannels[Case].Mode); // the concurrent edit itself still landed
    }

    [Fact]
    public void ThreeStrikesFailSafe_ReleaseIsNotASecondWriteInTheSameTick()
    {
        var h = new H();
        h.B.FailWrite = id => id == Case;
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.Tick(50); h.Tick(50); // two failures banked

        int percentWrites = 0, autoWrites = 0;
        h.B.OnSetPercent = (_, _) => percentWrites++;
        h.Tick(50); // 3rd failure: fail-safe trips and releases the channel via a follow-up SetAuto round

        autoWrites = h.WritesFor(Case).Count(w => w.Item2 is null);
        Assert.Equal(1, percentWrites);                          // exactly one write attempt this tick
        Assert.Equal(1, autoWrites);                              // exactly one release, not a second write
        Assert.Equal(FanMode.Auto, h.S.FanChannels[Case].Mode);
    }

    // ---- RestoreAll racing a mid-flight Tick (the generation latch) ----

    [Fact]
    public void RestoreAll_DuringPhase2_QueuedWriteIsSkipped_AndFanStaysReleased()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        h.C.SetMode(Gpu, FanMode.Manual); h.C.SetManualPercent(Gpu, 60);
        // Both channels are latched InSoftware + queued for a write in phase 1 (backend channel order is
        // Case, Gpu, Pump). Calling RestoreAll from Case's SetPercent — the first queued write — models
        // App.OnExit's poller.Stop()-then-RestoreAll() landing mid-Tick: it releases both already-latched
        // channels before Gpu's own queued write reaches the backend.
        h.B.OnSetPercent = (id, _) => { if (id == Case) h.C.RestoreAll(); };

        h.Tick(50);

        Assert.DoesNotContain(h.WritesFor(Gpu), w => w.Item2 == 60f); // Gpu's queued write never reached the backend
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Gpu).Status);
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Case).Status);
        Assert.False(h.M.Present); // both channels end the tick released — no armed marker should survive
    }

    [Fact]
    public void RestoreAll_BetweenWriteAndPhase3_ChannelIsReleasedAgain_AndMarkerConsistent()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);
        // The write lands normally, then RestoreAll runs before phase 2 returns — modeling App.OnExit's
        // RestoreAll() landing after the backend already re-drove the fan but before Tick's phase 3 can see it.
        h.B.OnSetPercent = (id, _) => { if (id == Case) h.C.RestoreAll(); };

        h.Tick(50);

        // SetPercent(40) actually reached the backend, then RestoreAll's SetAuto ran (releasing the channel out
        // from under the in-flight write), then phase 3 detects the generation changed, re-latches, and queues
        // its own follow-up SetAuto — so the channel is *not* left driving the fan at 40 with no marker.
        Assert.Equal(new (string, float?)[] { (Case, 40f), (Case, null), (Case, null) }, h.WritesFor(Case));
        Assert.Equal(FanChannelStatus.Idle, h.C.Views().Single(v => v.Id == Case).Status);
        // Marker semantics chosen here: by the end of the tick the channel really is released again (the
        // follow-up SetAuto succeeded), so UpdateMarkerLocked clears it in FinishTickLocked — the marker ends
        // the tick *absent*, not present. It was re-armed transiently in between (phase 3 re-latches it before
        // queuing the follow-up release, since at that instant the hardware may still be driven), but that
        // transient state isn't observable between Tick calls, so we only assert the post-Tick value.
        Assert.False(h.M.Present);
    }

    [Fact]
    public void ManualWrite_NoRestoreAllInterleaving_StillYieldsActiveAndOneWrite()
    {
        var h = new H();
        h.C.SetMode(Case, FanMode.Manual); h.C.SetManualPercent(Case, 40);

        h.Tick(50);

        Assert.Equal(new (string, float?)[] { (Case, 40f) }, h.WritesFor(Case));
        var v = h.C.Views().Single(x => x.Id == Case);
        Assert.Equal(FanChannelStatus.Active, v.Status);
        Assert.Equal(40f, v.Percent);
    }
}
