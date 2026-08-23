using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class FansViewModelTests
{
    private sealed class FakeBackend : IFanControlBackend
    {
        public List<FanChannel> Chans = new();
        public List<(string Id, float? Pct)> Writes = new();
        public IReadOnlyList<FanChannel> Channels => Chans;
        public void SetPercent(string id, float p) => Writes.Add((id, p));
        public void SetAuto(string id) => Writes.Add((id, null));
    }

    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.tctl", "Core (Tctl/Tdie)", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("gpu.core", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("cpu.load", "CPU Total", MetricGroup.Cpu, "Ryzen", "%"),
        new("cooler.liquid", "Liquid Temperature", MetricGroup.Cooler, "MSI CoreLiquid S360", "°C", "F1"),
    };

    private static (FansViewModel Vm, FanController C, FakeBackend B, AppSettings S) Make(
        Func<IEnumerable<string>>? procs = null, Func<DateTime>? clock = null, Action? saveSettings = null,
        Func<FanController, AppSettings, GameModeSwitcher>? switcher = null)
    {
        var b = new FakeBackend();
        b.Chans.Add(new FanChannel("/ite/control/0", "Fan #1", "ITE IT8696E", "mb.fan1", null, 0, 100));
        b.Chans.Add(new FanChannel("/ite/control/1", "Fan #2", "ITE IT8696E", "mb.fan2", null, 0, 100));
        b.Chans.Add(new FanChannel("/gpu/control/1", "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
        var s = new AppSettings();
        s.TilePrefs["cpu.tctl"] = new TilePref { Name = "CPU" };
        var c = new FanController(b, s, () => { });
        return (new FansViewModel(c, Defs, s, procs, clock, saveSettings, switcher?.Invoke(c, s)), c, b, s);
    }

    [Fact]
    public void Devices_GroupedInBackendOrder_WithChannels()
    {
        var (vm, _, _, _) = Make();
        Assert.True(vm.HasChannels);
        Assert.Equal(new[] { "ITE IT8696E", "RTX 5070 Ti" }, vm.Devices.Select(d => d.Device));
        Assert.Equal(new[] { "Fan #1", "Fan #2" }, vm.Devices[0].Channels.Select(c => c.Name));
        Assert.Equal(30f, vm.Devices[1].Channels[0].MinPercent);
    }

    [Fact]
    public void SourceOptions_AreOnlyCelsiusMetrics_WithFriendlyNames()
    {
        var (vm, _, _, _) = Make();
        var opts = vm.Devices[0].Channels[0].SourceOptions;
        Assert.Equal(new[] { "cpu.tctl", "gpu.core", "cooler.liquid" }, opts.Select(o => o.Id));
        Assert.Equal("CPU", opts[0].Label);                                  // TilePref rename wins
        Assert.Equal("Liquid Temperature", opts[2].Label);
    }

    [Fact]
    public void Edits_FlowToController_AndSettings()
    {
        var (vm, c, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Curve;
        ch.SourceSelections.Single(x => x.Id == "cpu.tctl").IsSelected = true;
        ch.ManualPercent = 66;
        ch.Name = "Front intake";
        Assert.True(ch.IsCurve); Assert.False(ch.IsManual);
        var p = s.FanChannels["/ite/control/0"];
        Assert.Equal(FanMode.Curve, p.Mode);
        Assert.Equal("cpu.tctl", p.SourceMetricId);
        Assert.Equal(66f, p.ManualPercent);
        Assert.Equal("Front intake", p.Name);
        Assert.Equal("Front intake", c.Views()[0].Name);
    }

    [Fact]
    public void Points_ReplaceInCollection_PersistsValidCurve_IgnoresInvalid()
    {
        var (vm, _, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Points[0] = new FanPoint(35, 30);
        Assert.Equal(35f, s.FanChannels["/ite/control/0"].Points[0].TempC);
        ch.Points.Clear();                          // 0 points → invalid → settings untouched
        Assert.Equal(4, s.FanChannels["/ite/control/0"].Points.Count);
    }

    [Fact]
    public void Refresh_ReflectsControllerViews()
    {
        var (vm, c, _, s) = Make();
        s.FanControlEnabled = true;
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Curve; ch.SourceSelections.Single(x => x.Id == "cpu.tctl").IsSelected = true;
        c.Tick(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.tctl"] = 60f, ["mb.fan1"] = 1450f }, DateTime.UtcNow), DateTime.UtcNow);
        vm.Refresh();
        Assert.Equal("1450 RPM", ch.RpmText);
        Assert.Equal("60 %", ch.PercentText);      // default curve at 60 °C = 60 %
        Assert.Equal("60 %", ch.TargetText);
        Assert.Equal("60.0 °C", ch.SourceTempText);
        Assert.Equal(60.0, ch.LiveTemp, 3);
        Assert.Equal(60.0, ch.LiveTarget, 3);
        Assert.Equal("Active", ch.StatusText);
    }

    [Fact]
    public void Refresh_DoesNotEchoBackIntoController()
    {
        var (vm, c, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        ch.Mode = FanMode.Manual;
        int before = s.FanChannels.Count;
        vm.Refresh(); vm.Refresh();
        Assert.Equal(before, s.FanChannels.Count);
        Assert.Equal(FanMode.Manual, c.Views()[0].Mode);
    }

    [Fact]
    public void Enabled_TwoWayWithController()
    {
        var (vm, c, _, s) = Make();
        Assert.False(vm.Enabled);
        vm.Enabled = true;
        Assert.True(c.Enabled); Assert.True(s.FanControlEnabled);
    }

    [Fact]
    public void SetAllAuto_SetsEveryChannelAuto()
    {
        var (vm, c, _, _) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.Devices[1].Channels[0].Mode = FanMode.Curve;
        vm.SetAllAutoCommand.Execute(null);
        Assert.All(c.Views(), v => Assert.Equal(FanMode.Auto, v.Mode));
        Assert.All(vm.Devices.SelectMany(d => d.Channels), ch => Assert.Equal(FanMode.Auto, ch.Mode));
    }

    [Fact]
    public void SourceSelections_TogglingPersistsList_AndSummary()
    {
        var (vm, c, _, s) = Make();
        var ch = vm.Devices[0].Channels[0];
        Assert.Equal(3, ch.SourceSelections.Count);
        ch.SourceSelections[0].IsSelected = true;   // cpu.tctl
        ch.SourceSelections[2].IsSelected = true;   // cooler.liquid
        Assert.Equal(new[] { "cpu.tctl", "cooler.liquid" }, s.FanChannels["/ite/control/0"].SourceMetricIds);
        Assert.Equal("Sources (2)", ch.SourceSummary);
        ch.SourceSelections[0].IsSelected = false;
        Assert.Equal(new[] { "cooler.liquid" }, s.FanChannels["/ite/control/0"].SourceMetricIds);
        Assert.Equal("Sources (1)", ch.SourceSummary);
    }

    [Fact]
    public void NoChannels_HasChannelsFalse()
    {
        var c = new FanController(new FakeBackend(), new AppSettings(), () => { });
        var vm = new FansViewModel(c, Defs, new AppSettings());
        Assert.False(vm.HasChannels);
        Assert.Empty(vm.Devices);
    }

    [Fact]
    public void RecoveryNotice_DefaultsEmpty_DismissClearsIt()
    {
        var (vm, _, _, _) = Make();
        Assert.Equal("", vm.RecoveryNotice);
        Assert.False(vm.HasRecoveryNotice);
        vm.RecoveryNotice = "Stats did not shut down cleanly last time — all fans were returned to device control.";
        Assert.True(vm.HasRecoveryNotice);
        vm.DismissRecoveryNoticeCommand.Execute(null);
        Assert.Equal("", vm.RecoveryNotice);
        Assert.False(vm.HasRecoveryNotice);
    }

    [Fact]
    public void Conflicts_ThrottledToFiveSeconds_AndFormatted()
    {
        int calls = 0; var now = new DateTime(2026, 8, 23, 12, 0, 0, DateTimeKind.Utc);
        var (vm, _, _, _) = Make(() => { calls++; return new[] { "FanControl", "MSI Center" }; }, () => now);
        vm.Refresh(); Assert.Equal(1, calls);
        Assert.True(vm.HasConflict);
        Assert.StartsWith("Detected: Fan Control, MSI Center", vm.ConflictText);
        now = now.AddSeconds(3); vm.Refresh(); Assert.Equal(1, calls);
        now = now.AddSeconds(3); vm.Refresh(); Assert.Equal(2, calls);
    }

    [Fact]
    public void Profiles_SaveLoadDelete_CreateDefaults()
    {
        var (vm, c, _, s) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.SaveProfileCommand.Execute("Quiet");
        Assert.Equal(new[] { "Quiet" }, vm.ProfileNames);
        Assert.Equal("Quiet", vm.ActiveProfileName);
        vm.Devices[0].Channels[0].Mode = FanMode.Auto;
        Assert.Equal("Custom", vm.ActiveProfileName);
        vm.LoadProfileCommand.Execute("Quiet");
        Assert.Equal(FanMode.Manual, vm.Devices[0].Channels[0].Mode);
        Assert.Equal("Quiet", vm.ActiveProfileName);
        vm.CreateDefaultProfilesCommand.Execute(null);
        Assert.Equal(new[] { "Quiet", "Silent", "Balanced", "Gaming" }, vm.ProfileNames);
        Assert.Equal(new[] { "cpu.tctl" }, s.FanProfiles.Single(p => p.Name == "Gaming").Channels["/ite/control/0"].SourceMetricIds);
        Assert.Equal(new[] { "gpu.core" }, s.FanProfiles.Single(p => p.Name == "Gaming").Channels["/gpu/control/1"].SourceMetricIds);
        vm.DeleteProfileCommand.Execute("Quiet");
        Assert.DoesNotContain("Quiet", vm.ProfileNames);
        Assert.Equal("Custom", vm.ActiveProfileName);
    }

    [Fact]
    public void GameMode_SettingsRoundTrip_AndStatus()
    {
        int saves = 0;
        var (vm, _, _, s) = Make(saveSettings: () => saves++, switcher: (c, st) => new GameModeSwitcher(c, st));
        Assert.Equal("Game mode: off", vm.GameModeStatus);

        vm.GameModeEnabled = true;
        vm.GamingProfile = "Gaming";
        vm.DesktopProfile = "Silent";

        Assert.True(s.GameModeEnabled);
        Assert.Equal("Gaming", s.GameModeGamingProfile);
        Assert.Equal("Silent", s.GameModeDesktopProfile);
        Assert.Equal(3, saves);
        Assert.Equal("Game mode: desktop", vm.GameModeStatus);
    }
}
