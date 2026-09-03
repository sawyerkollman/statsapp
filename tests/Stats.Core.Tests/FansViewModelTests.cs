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
        Func<FanController, AppSettings, GameModeSwitcher>? switcher = null, Action<AppSettings>? seed = null,
        bool? hardwareEnabledAtStartup = null)
    {
        var b = new FakeBackend();
        b.Chans.Add(new FanChannel("/ite/control/0", "Fan #1", "ITE IT8696E", "mb.fan1", null, 0, 100));
        b.Chans.Add(new FanChannel("/ite/control/1", "Fan #2", "ITE IT8696E", "mb.fan2", null, 0, 100));
        b.Chans.Add(new FanChannel("/gpu/control/1", "GPU Fan 1", "RTX 5070 Ti", null, null, 30, 100));
        var s = new AppSettings();
        s.TilePrefs["cpu.tctl"] = new TilePref { Name = "CPU" };
        seed?.Invoke(s);
        // Never fall through to the production process scan: it enumerates the build machine's real process table
        // (slow, and a dev box running iCUE would put text in ConflictText mid-test).
        procs ??= Array.Empty<string>;
        // Same callback for both: some VM commands persist through the controller (SetActiveProfile) and some
        // through the injected delegate, and a test counting saves has to see either one.
        var c = new FanController(b, s, saveSettings ?? (() => { }));
        return (new FansViewModel(c, Defs, s, procs, clock, saveSettings, switcher?.Invoke(c, s), hardwareEnabledAtStartup), c, b, s);
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
        string[] running = { "FanControl", "MSI Center" };
        var (vm, _, _, _) = Make(() => { calls++; return running; }, () => now);
        vm.Refresh(); Assert.Equal(1, calls);
        Assert.True(vm.HasConflict);
        Assert.StartsWith("Detected: Fan Control, MSI Center", vm.ConflictText);
        now = now.AddSeconds(3); vm.Refresh(); Assert.Equal(1, calls);
        now = now.AddSeconds(3); vm.Refresh(); Assert.Equal(2, calls);

        // The banner must come down once the user does what it asked, or it teaches them to ignore it.
        running = new[] { "explorer" };
        now = now.AddSeconds(6); vm.Refresh(); Assert.Equal(3, calls);
        Assert.False(vm.HasConflict);
        Assert.Equal("", vm.ConflictText);
    }

    [Fact]
    public void Profiles_SaveLoadDelete_CreateDefaults()
    {
        int saves = 0;
        var (vm, c, _, s) = Make(saveSettings: () => saves++);
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.SaveProfileCommand.Execute("Quiet");
        Assert.Equal(new[] { "Quiet" }, vm.ProfileNames);
        Assert.Equal("Quiet", vm.ActiveProfileName);
        vm.Devices[0].Channels[0].Mode = FanMode.Auto;
        Assert.Equal("Custom", vm.ActiveProfileName);
        vm.LoadProfileCommand.Execute("Quiet");
        Assert.Equal(FanMode.Manual, vm.Devices[0].Channels[0].Mode);
        Assert.Equal("Quiet", vm.ActiveProfileName);
        int beforeCreate = saves;
        vm.CreateDefaultProfilesCommand.Execute(null);
        Assert.True(saves > beforeCreate, "CreateDefaultProfiles must persist, or the three defaults vanish on restart");
        Assert.Equal(new[] { "Quiet", "Silent", "Balanced", "Gaming" }, vm.ProfileNames);
        Assert.Equal(new[] { "cpu.tctl" }, s.FanProfiles.Single(p => p.Name == "Gaming").Channels["/ite/control/0"].SourceMetricIds);
        Assert.Equal(new[] { "gpu.core" }, s.FanProfiles.Single(p => p.Name == "Gaming").Channels["/gpu/control/1"].SourceMetricIds);
        int beforeDelete = saves;
        vm.DeleteProfileCommand.Execute("Quiet");
        Assert.True(saves > beforeDelete, "DeleteProfile must persist, or the deleted profile comes back on restart");
        Assert.DoesNotContain("Quiet", vm.ProfileNames);
        Assert.Equal("Custom", vm.ActiveProfileName);
    }

    [Fact]
    public void GameMode_SettingsRoundTrip_AndStatus()
    {
        int saves = 0;
        var (vm, _, _, s) = Make(saveSettings: () => saves++, switcher: (c, st) => new GameModeSwitcher(c, st));
        Assert.Equal("Game mode: off", vm.GameModeStatus);
        // GameModeChanged is the only thing that keeps PresentMon alive for game mode (App subscribes
        // ApplyFrameTracing to it). If it stops firing, fps.avg is never produced and the feature silently
        // does nothing.
        int reapply = 0;
        vm.GameModeChanged += () => reapply++;

        vm.GameModeEnabled = true;
        vm.GamingProfile = "Gaming";
        vm.DesktopProfile = "Silent";

        Assert.True(s.GameModeEnabled);
        Assert.Equal("Gaming", s.GameModeGamingProfile);
        Assert.Equal("Silent", s.GameModeDesktopProfile);
        Assert.Equal(3, saves);
        Assert.Equal(3, reapply);
        Assert.Equal("Game mode: desktop", vm.GameModeStatus);
    }

    [Fact]
    public void Constructor_WithSavedSources_DoesNotWriteBackToController()
    {
        int saves = 0;
        var (_, _, _, s) = Make(saveSettings: () => saves++, seed: st =>
        {
            st.FanChannels["/ite/control/0"] = new FanChannelPref { Mode = FanMode.Curve, SourceMetricIds = new() { "cpu.tctl" }, SourceMetricId = "cpu.tctl" };
            st.ActiveFanProfile = "P";
        });
        // Building the source checkboxes must not echo the saved selection back through SetSources: that would
        // drop the active profile to "Custom" and persist it just for opening the Fans window.
        Assert.Equal(0, saves);
        Assert.Equal("P", s.ActiveFanProfile);
        Assert.Equal(new[] { "cpu.tctl" }, s.FanChannels["/ite/control/0"].SourceMetricIds);
    }

    [Fact]
    public void SelectedProfileName_FollowsTheActiveProfile_AndPickingADifferentOneLoadsIt()
    {
        var (vm, c, _, s) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.SaveProfileCommand.Execute("Quiet");
        Assert.Equal("Quiet", vm.SelectedProfileName);          // SyncProfileState follows ActiveProfile quietly
        vm.Devices[0].Channels[0].Mode = FanMode.Curve;
        vm.SaveProfileCommand.Execute("Curvy");
        Assert.Equal("Curvy", vm.SelectedProfileName);

        vm.SelectedProfileName = "Quiet";                       // picking a DIFFERENT entry loads it
        Assert.Equal(FanMode.Manual, vm.Devices[0].Channels[0].Mode);
        Assert.Equal("Quiet", vm.ActiveProfileName);
        Assert.Equal("Quiet", vm.SelectedProfileName);

        vm.Devices[0].Channels[0].ManualPercent = 44;           // an edit makes the applied profile "Custom" …
        vm.Refresh(); vm.Refresh();
        Assert.Equal("Custom", vm.ActiveProfileName);
        Assert.Null(vm.SelectedProfileName);                    // … and the dropdown quietly clears (no reload)
        Assert.Equal(44f, s.FanChannels["/ite/control/0"].ManualPercent); // Refresh did not re-load the profile
        Assert.Equal(44f, c.Views().Single(v => v.Id == "/ite/control/0").ManualPercent);
    }

    [Fact]
    public void SelectedProfileName_ClearsOnlyWhenTheProfileIsGone()
    {
        var (vm, _, _, _) = Make();
        vm.SaveProfileCommand.Execute("Quiet");
        Assert.Equal("Quiet", vm.SelectedProfileName);
        vm.DeleteProfileCommand.Execute("Quiet");
        Assert.Null(vm.SelectedProfileName);
    }

    [Fact]
    public void IsModified_TracksAnEditAfterLoad_AndReloadReappliesTheSameProfile()
    {
        var (vm, c, _, s) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.Devices[0].Channels[0].ManualPercent = 40;
        vm.SaveProfileCommand.Execute("Quiet");
        Assert.False(vm.IsModified);

        vm.Devices[0].Channels[0].ManualPercent = 77;           // user edit after load/save
        vm.Refresh();
        Assert.True(vm.IsModified);
        Assert.Equal("Custom", vm.ActiveProfileName);
        Assert.Null(vm.SelectedProfileName);

        vm.ReloadCommand.Execute(null);                         // re-applies "Quiet" without re-picking it
        Assert.Equal("Quiet", vm.ActiveProfileName);
        Assert.Equal("Quiet", vm.SelectedProfileName);
        Assert.False(vm.IsModified);
        Assert.Equal(40f, s.FanChannels["/ite/control/0"].ManualPercent);
        Assert.Equal(40f, c.Views().Single(v => v.Id == "/ite/control/0").ManualPercent);
    }

    [Fact]
    public void Reload_OfTheSameProfile_WorksRepeatedly()
    {
        // The v1.4 problem: picking an already-selected ComboBox entry raises no change event, so re-loading the
        // same profile twice in a row (edit, reload, edit again, reload again) has to go through the Reload
        // command rather than the dropdown, regardless of what the dropdown currently shows.
        var (vm, c, _, s) = Make();
        vm.Devices[0].Channels[0].Mode = FanMode.Manual;
        vm.Devices[0].Channels[0].ManualPercent = 50;
        vm.SaveProfileCommand.Execute("Quiet");

        vm.Devices[0].Channels[0].ManualPercent = 10;
        vm.Refresh();
        vm.ReloadCommand.Execute(null);
        Assert.Equal(50f, s.FanChannels["/ite/control/0"].ManualPercent);
        Assert.False(vm.IsModified);

        vm.Devices[0].Channels[0].ManualPercent = 20;
        vm.Refresh();
        Assert.True(vm.IsModified);
        vm.ReloadCommand.Execute(null);
        Assert.Equal(50f, s.FanChannels["/ite/control/0"].ManualPercent);
        Assert.Equal(50f, c.Views().Single(v => v.Id == "/ite/control/0").ManualPercent);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void Reload_WithNoProfileEverLoaded_IsANoOp()
    {
        var (vm, _, _, _) = Make();
        Assert.False(vm.IsModified);
        vm.ReloadCommand.Execute(null); // must not throw
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void DeleteProfile_ClearsGameModePicksThatNameIt()
    {
        int changed = 0;
        var (vm, _, _, s) = Make();
        vm.SaveProfileCommand.Execute("Quiet");
        vm.SaveProfileCommand.Execute("Loud");
        vm.GamingProfile = "Quiet";
        vm.DesktopProfile = "Loud";
        vm.GameModeChanged += () => changed++;

        vm.DeleteProfileCommand.Execute("Quiet");

        Assert.Null(s.GameModeGamingProfile);       // erased deliberately and in order …
        Assert.Equal("Loud", s.GameModeDesktopProfile); // … and only the one that named the deleted profile
        Assert.Equal(1, changed);
    }

    [Fact]
    public void GameModePick_NamingAMissingProfile_SurvivesComboBoxCoercion()
    {
        var (vm, _, _, s) = Make(seed: st => st.GameModeGamingProfile = "Gone");
        Assert.Equal("Gone", vm.GamingProfile);
        vm.GamingProfile = null;                    // WPF cannot select an item absent from ItemsSource
        Assert.Equal("Gone", s.GameModeGamingProfile);
    }

    [Fact]
    public void IdentifyCommand_CanExecute_FollowsTheMasterSwitch()
    {
        var (vm, c, b, _) = Make();
        var ch = vm.Devices[0].Channels[0];
        Assert.False(ch.IdentifyCommand.CanExecute(null)); // master switch defaults off

        vm.Enabled = true;
        Assert.True(ch.IdentifyCommand.CanExecute(null));
        ch.IdentifyCommand.Execute(null);
        c.Tick(new SensorSnapshot(new Dictionary<string, float?> { ["mb.fan1"] = 1200f }, DateTime.UtcNow), DateTime.UtcNow);
        Assert.Contains(b.Writes, w => w.Id == "/ite/control/0" && w.Pct == 100f);

        vm.Enabled = false;
        Assert.False(ch.IdentifyCommand.CanExecute(null));
    }

    [Fact]
    public void SafetyBanner_DefaultsExpanded_GotItCollapsesAndPersists()
    {
        int saves = 0;
        var (vm, _, _, s) = Make(saveSettings: () => saves++);
        Assert.False(vm.SafetyBannerCollapsed);
        vm.DismissSafetyBannerCommand.Execute(null);
        Assert.True(vm.SafetyBannerCollapsed);
        Assert.True(s.FanSafetyBannerCollapsed);
        Assert.Equal(1, saves);
        vm.DismissSafetyBannerCommand.Execute(null); // idempotent — no extra save
        Assert.Equal(1, saves);
    }

    [Fact]
    public void UnavailableText_JudgesTheSettingTheReaderWasBuiltWith()
    {
        var c = new FanController(new FakeBackend(), new AppSettings(), () => { });
        var justEnabled = new AppSettings { ReadMotherboardAndCoolers = true };
        var vm = new FansViewModel(c, Defs, justEnabled, hardwareEnabledAtStartup: false);
        Assert.Contains("restart Stats to apply the hardware setting you changed", vm.UnavailableText);

        var off = new AppSettings { ReadMotherboardAndCoolers = false };
        var vm2 = new FansViewModel(c, Defs, off, hardwareEnabledAtStartup: false);
        Assert.Contains("enable", vm2.UnavailableText);

        var on = new AppSettings { ReadMotherboardAndCoolers = true };
        var vm3 = new FansViewModel(c, Defs, on, hardwareEnabledAtStartup: true);
        Assert.Contains("degraded mode", vm3.UnavailableText);
    }
}
