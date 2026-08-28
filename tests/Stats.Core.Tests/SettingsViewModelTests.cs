using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class SettingsViewModelTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.ppt", "CPU PPT", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("cpu.tdc", "CPU TDC", MetricGroup.Cpu, "Ryzen", "A", "F1"),
        new("cpu.temp", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("gpu.power", "RTX · GPU Package", MetricGroup.Gpu, "RTX", "W", "F1"),
        new("gpu.clock", "RTX · GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
    };

    private static (SettingsViewModel Vm, AppSettings S, List<SettingsChange> Changes, Func<int> Saves) Make()
    {
        var s = new AppSettings { ThresholdRules = ThresholdDefaults.Rules(), MetricLimits = { ["cpu.ppt"] = 150f } };
        var changes = new List<SettingsChange>();
        int saves = 0;
        var vm = new SettingsViewModel(s, Defs, () => saves++);
        vm.Changed += c => changes.Add(c);
        return (vm, s, changes, () => saves);
    }

    [Fact]
    public void Ctor_LoadsValues_WithoutRaisingChanged()
    {
        var (vm, _, changes, saves) = Make();
        Assert.Equal(1.0, vm.PollIntervalSeconds);
        Assert.Equal(85f, vm.CpuTempWarn);
        Assert.Equal(88f, vm.GpuTempCrit);
        Assert.Equal(90f, vm.LoadWarn);
        Assert.Equal("Ctrl+Shift+O", vm.OverlayHotkey);
        Assert.Empty(changes);
        Assert.Equal(0, saves());
    }

    [Fact]
    public void PollInterval_WritesThroughClampedAndRaises()
    {
        var (vm, s, changes, saves) = Make();
        vm.PollIntervalSeconds = 9;
        Assert.Equal(5.0, s.PollIntervalSeconds);
        Assert.Equal(5.0, vm.PollIntervalSeconds);
        Assert.Equal(new[] { SettingsChange.PollInterval }, changes);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void HistoryWindow_WritesThroughAndRaises()
    {
        var (vm, s, changes, _) = Make();
        vm.HistoryWindowMinutes = 15;
        Assert.Equal(15, s.HistoryWindowMinutes);
        Assert.Contains(SettingsChange.HistoryWindow, changes);
    }

    [Fact]
    public void HistoryWindow_SnapsBothSettingsAndViewModel()
    {
        var (vm, s, _, _) = Make();
        vm.HistoryWindowMinutes = 7;
        Assert.Equal(5, s.HistoryWindowMinutes);
        Assert.Equal(5, vm.HistoryWindowMinutes);
    }

    [Fact]
    public void Thresholds_ValidPair_UpdatesRulesAndRaises()
    {
        var (vm, s, changes, _) = Make();
        vm.CpuTempWarn = 80f;
        vm.CpuTempCrit = 90f;
        var rule = s.ThresholdRules.Single(r => r.Group == MetricGroup.Cpu && r.Unit == "°C");
        Assert.Equal(80f, rule.Warn);
        Assert.Equal(90f, rule.Crit);
        Assert.Equal("", vm.ThresholdError);
        Assert.Contains(SettingsChange.Thresholds, changes);
    }

    [Fact]
    public void Thresholds_WarnNotBelowCrit_NotAppliedAndFlagged()
    {
        var (vm, s, _, _) = Make();
        vm.GpuTempWarn = 95f; // crit is 88
        var rule = s.ThresholdRules.Single(r => r.Group == MetricGroup.Gpu && r.Unit == "°C");
        Assert.Equal(80f, rule.Warn);
        Assert.NotEqual("", vm.ThresholdError);
        vm.GpuTempCrit = 99f; // now valid
        Assert.Equal(95f, rule.Warn);
        Assert.Equal(99f, rule.Crit);
        Assert.Equal("", vm.ThresholdError);
    }

    [Fact]
    public void LoadThresholds_ApplyToBothCpuAndGpuPercentRules()
    {
        var (vm, s, _, _) = Make();
        vm.LoadWarn = 70f;
        Assert.All(s.ThresholdRules.Where(r => r.Unit == "%"), r => Assert.Equal(70f, r.Warn));
    }

    [Fact]
    public void LimitItems_OnlyCpuPowerCurrentAndGpuPower_WithExistingValues()
    {
        var (vm, _, _, _) = Make();
        Assert.Equal(new[] { "cpu.ppt", "cpu.tdc", "gpu.power" }, vm.LimitItems.Select(i => i.Definition.Id));
        Assert.Equal("150", vm.LimitItems[0].ValueText);
        Assert.Equal("", vm.LimitItems[1].ValueText);
    }

    [Fact]
    public void LimitItem_Edit_ParsesRemovesAndFlagsInvalid()
    {
        var (vm, s, changes, _) = Make();
        vm.LimitItems[1].ValueText = "180";
        Assert.Equal(180f, s.MetricLimits["cpu.tdc"]);
        Assert.False(vm.LimitItems[1].IsInvalid);
        vm.LimitItems[0].ValueText = "";
        Assert.False(s.MetricLimits.ContainsKey("cpu.ppt"));
        vm.LimitItems[2].ValueText = "abc";
        Assert.True(vm.LimitItems[2].IsInvalid);
        Assert.False(s.MetricLimits.ContainsKey("gpu.power"));
        Assert.Contains(SettingsChange.Limits, changes);
    }

    [Fact]
    public void Overlay_PropsWriteThroughAndRaise()
    {
        var (vm, s, changes, _) = Make();
        vm.OverlayIsVertical = true;
        vm.OverlayFontScale = 1.5;
        vm.OverlayOpacity = 0.6;
        vm.OverlayClickThrough = true;
        Assert.Equal(OverlayOrientation.Vertical, s.OverlayOrientation);
        Assert.Equal(1.5, s.OverlayFontScale);
        Assert.Equal(0.6, s.OverlayOpacity);
        Assert.True(s.OverlayClickThrough);
        Assert.Equal(4, changes.Count(c => c == SettingsChange.Overlay));
    }

    [Fact]
    public void Hotkey_ValidNormalizes_InvalidFlagged_EmptyDisables()
    {
        var (vm, s, changes, _) = Make();
        vm.OverlayHotkey = "ctrl+alt+p";
        Assert.Equal("Ctrl+Alt+P", s.OverlayHotkey);
        Assert.Equal("", vm.HotkeyStatus);
        Assert.Contains(SettingsChange.Hotkey, changes);
        vm.OverlayHotkey = "banana";
        Assert.Equal("Ctrl+Alt+P", s.OverlayHotkey); // unchanged
        Assert.Equal("Invalid hotkey", vm.HotkeyStatus);
        vm.OverlayHotkey = "";
        Assert.Equal("", s.OverlayHotkey);
        Assert.Equal("Hotkey disabled", vm.HotkeyStatus);
    }

    [Fact]
    public void ShowCoreMatrix_WritesThroughAndRaises()
    {
        var (vm, s, changes, _) = Make();
        vm.ShowCoreMatrix = false;
        Assert.False(s.ShowCoreMatrix);
        Assert.Contains(SettingsChange.CoreMatrix, changes);
    }

    [Fact]
    public void ResetOverlayPosition_RaisesRequest()
    {
        var (vm, _, _, _) = Make();
        int n = 0;
        vm.OverlayPositionResetRequested += () => n++;
        vm.ResetOverlayPositionCommand.Execute(null);
        Assert.Equal(1, n);
    }

    [Fact]
    public void FpsThresholds_LoadFromRule_WarnMustExceedCrit()
    {
        var s = new AppSettings { ThresholdRules = ThresholdDefaults.Rules() };
        int saves = 0;
        var vm = new SettingsViewModel(s, Array.Empty<MetricDefinition>(), () => saves++);
        Assert.Equal(60f, vm.FpsWarn);
        Assert.Equal(30f, vm.FpsCrit);
        vm.FpsWarn = 20; // below crit → invalid
        Assert.Equal("FPS: warn must be above crit", vm.ThresholdError);
        Assert.Equal(60f, s.ThresholdRules.Single(r => r.Group == MetricGroup.Game && r.Unit == "fps").Warn); // not applied
        vm.FpsWarn = 75;
        Assert.Equal("", vm.ThresholdError);
        Assert.Equal(75f, s.ThresholdRules.Single(r => r.Group == MetricGroup.Game && r.Unit == "fps").Warn);
        Assert.True(s.ThresholdRules.Single(r => r.Group == MetricGroup.Game && r.Unit == "fps").LowerIsWorse);
    }

    [Fact]
    public void ReadMotherboardAndCoolers_RaisesHardware_AndPersists()
    {
        var s = new AppSettings(); int saves = 0; SettingsChange? last = null;
        var vm = new SettingsViewModel(s, Array.Empty<MetricDefinition>(), () => saves++);
        vm.Changed += c => last = c;
        vm.ReadMotherboardAndCoolers = false;
        Assert.False(s.ReadMotherboardAndCoolers);
        Assert.Equal(SettingsChange.Hardware, last);
        Assert.Equal(1, saves);
    }

    [Fact]
    public void StartupEnabled_UserToggle_RaisesRequest_NoSaveNoChanged()
    {
        var (vm, _, changes, saves) = Make();
        var requests = new List<bool>();
        vm.StartupToggleRequested += requests.Add;

        vm.StartupEnabled = true;

        Assert.Equal(new[] { true }, requests);
        Assert.Empty(changes); // live OS state — never routed through SettingsChange
        Assert.Equal(0, saves()); // — nor persisted to AppSettings
    }

    [Fact]
    public void ApplyStartupState_UpdatesPropertiesWithoutRaisingToggleRequest()
    {
        var (vm, _, _, _) = Make();
        int requests = 0;
        vm.StartupToggleRequested += _ => requests++;

        vm.ApplyStartupState(enabled: true, busy: false, error: "boom");

        Assert.True(vm.StartupEnabled);
        Assert.False(vm.StartupBusy);
        Assert.Equal("boom", vm.StartupError);
        Assert.Equal(0, requests); // the query/mutation result must not loop back as a user toggle
    }

    [Fact]
    public void ApplyStartupState_ThenUserToggle_StillRaisesRequest()
    {
        var (vm, _, _, _) = Make();
        var requests = new List<bool>();
        vm.StartupToggleRequested += requests.Add;

        vm.ApplyStartupState(enabled: true, busy: false, error: "");
        vm.StartupEnabled = false; // a genuine user click after the suppressed write above

        Assert.Equal(new[] { false }, requests);
    }

    [Fact]
    public void Ctor_LoadsThemeFromSettings()
    {
        var s = new AppSettings { ThemePreset = "Dark Blue", ThemeAccent = "#123456" };
        var vm = new SettingsViewModel(s, Array.Empty<MetricDefinition>(), () => { });
        Assert.Equal("Dark Blue", vm.SelectedThemePreset);
        Assert.Equal("#123456", vm.AccentHex);
        Assert.False(vm.IsAccentInvalid);
    }

    [Fact]
    public void Ctor_NoAccentOverride_AccentHexIsEmpty()
    {
        var (vm, _, _, _) = Make();
        Assert.Equal("", vm.AccentHex);
    }

    [Fact]
    public void SelectedThemePreset_WritesThroughAndRaisesTheme()
    {
        var (vm, s, changes, saves) = Make();
        vm.SelectedThemePreset = "Dark Purple";
        Assert.Equal("Dark Purple", s.ThemePreset);
        Assert.Equal(new[] { SettingsChange.Theme }, changes);
        Assert.Equal(1, saves());
    }

    [Fact]
    public void SelectedThemePreset_UnknownValue_SanitizesToDefault()
    {
        var (vm, s, _, _) = Make();
        vm.SelectedThemePreset = "Neon Pink";
        Assert.Equal(ThemePresets.Default, vm.SelectedThemePreset);
        Assert.Equal(ThemePresets.Default, s.ThemePreset);
    }

    [Fact]
    public void AccentHex_Valid_WritesThroughAndRaisesTheme()
    {
        var (vm, s, changes, _) = Make();
        vm.AccentHex = "#4a9ee0";
        Assert.Equal("#4A9EE0", s.ThemeAccent);
        Assert.False(vm.IsAccentInvalid);
        Assert.Contains(SettingsChange.Theme, changes);
    }

    [Fact]
    public void AccentHex_Invalid_FlagsInvalid_DoesNotWriteOrRaise()
    {
        var (vm, s, changes, _) = Make();
        vm.AccentHex = "not-a-color";
        Assert.True(vm.IsAccentInvalid);
        Assert.Null(s.ThemeAccent);
        Assert.DoesNotContain(SettingsChange.Theme, changes);
    }

    [Fact]
    public void AccentHex_Empty_ClearsOverride()
    {
        var (vm, s, changes, _) = Make();
        vm.AccentHex = "#4a9ee0";
        vm.AccentHex = "";
        Assert.Null(s.ThemeAccent);
        Assert.False(vm.IsAccentInvalid);
        Assert.Equal(2, changes.Count(c => c == SettingsChange.Theme));
    }

    [Fact]
    public void ResetAccentCommand_ClearsAccentHex()
    {
        var (vm, s, _, _) = Make();
        vm.AccentHex = "#4a9ee0";
        vm.ResetAccentCommand.Execute(null);
        Assert.Equal("", vm.AccentHex);
        Assert.Null(s.ThemeAccent);
    }

    [Fact]
    public void SetAccentCommand_SetsAccentHexFromSwatch()
    {
        var (vm, s, _, _) = Make();
        vm.SetAccentCommand.Execute("#4FC06A");
        Assert.Equal("#4FC06A", vm.AccentHex);
        Assert.Equal("#4FC06A", s.ThemeAccent);
    }

    [Fact]
    public void ThemePresetNamesAndAccentSwatches_ExposeCoreData()
    {
        var (vm, _, _, _) = Make();
        Assert.Equal(ThemePresets.Names, vm.ThemePresetNames);
        Assert.Equal(ThemePresets.AccentSwatches, vm.AccentSwatches);
    }

    // ---- About section: version display, dev-build detection, manual check-for-updates (v1.7) ----

    [Fact]
    public void SetVersionInfo_PopulatesDisplayAndDevBuildFlag()
    {
        var (vm, _, _, _) = Make();
        vm.SetVersionInfo("v1.7.0", isDevBuild: false);
        Assert.Equal("v1.7.0", vm.AppVersionDisplay);
        Assert.False(vm.IsDevBuild);

        vm.SetVersionInfo("Development build", isDevBuild: true);
        Assert.Equal("Development build", vm.AppVersionDisplay);
        Assert.True(vm.IsDevBuild);
    }

    [Fact]
    public void CheckForUpdatesCommand_RaisesRequest_AndEntersBusyState_ClearingPriorResult()
    {
        var (vm, _, _, _) = Make();
        vm.ApplyManualCheckResult("Up to date"); // simulate a prior completed check
        var requests = 0;
        vm.CheckForUpdatesRequested += () => requests++;

        vm.CheckForUpdatesCommand.Execute(null);

        Assert.Equal(1, requests);
        Assert.True(vm.UpdateCheckBusy);
        Assert.Equal("", vm.UpdateCheckResult);
        Assert.False(vm.UpdateCheckFailed);
    }

    [Fact]
    public void CheckForUpdatesCommand_AlreadyBusy_IsNoOp()
    {
        var (vm, _, _, _) = Make();
        var requests = 0;
        vm.CheckForUpdatesRequested += () => requests++;

        vm.CheckForUpdatesCommand.Execute(null);
        vm.CheckForUpdatesCommand.Execute(null); // second click while the first is still in flight

        Assert.Equal(1, requests);
    }

    [Fact]
    public void CheckForUpdatesCommand_DevBuild_NeverRaisesRequest_OrCallsGitHub()
    {
        var (vm, _, _, _) = Make();
        vm.SetVersionInfo("Development build", isDevBuild: true);
        var requests = 0;
        vm.CheckForUpdatesRequested += () => requests++;

        vm.CheckForUpdatesCommand.Execute(null);

        Assert.Equal(0, requests);
        Assert.False(vm.UpdateCheckBusy);
    }

    [Fact]
    public void ApplyManualCheckResult_UpToDate_ClearsBusy_AndIsNotMarkedFailed()
    {
        var (vm, _, _, _) = Make();
        vm.CheckForUpdatesCommand.Execute(null);

        vm.ApplyManualCheckResult("Up to date");

        Assert.False(vm.UpdateCheckBusy);
        Assert.Equal("Up to date", vm.UpdateCheckResult);
        Assert.False(vm.UpdateCheckFailed);
    }

    [Fact]
    public void ApplyManualCheckResult_Failure_ClearsBusy_AndIsMarkedFailed()
    {
        var (vm, _, _, _) = Make();
        vm.CheckForUpdatesCommand.Execute(null);

        vm.ApplyManualCheckResult("Couldn't check for updates — try again later.", failed: true);

        Assert.False(vm.UpdateCheckBusy);
        Assert.Equal("Couldn't check for updates — try again later.", vm.UpdateCheckResult);
        Assert.True(vm.UpdateCheckFailed);
    }

    [Fact]
    public void ApplyManualCheckResult_UpdateFound_LeavesResultEmpty_SoBannerOwnsTheNews()
    {
        var (vm, _, _, _) = Make();
        vm.CheckForUpdatesCommand.Execute(null);

        vm.ApplyManualCheckResult(""); // a newer release was found; the dashboard banner carries the message

        Assert.False(vm.UpdateCheckBusy);
        Assert.Equal("", vm.UpdateCheckResult);
        Assert.False(vm.UpdateCheckFailed);
    }

    // ---- Hardware section: Restart now (v1.7) ----

    [Fact]
    public void RestartNowCommand_RaisesRestartRequested()
    {
        var (vm, _, _, _) = Make();
        var requests = 0;
        vm.RestartRequested += () => requests++;

        vm.RestartNowCommand.Execute(null);

        Assert.Equal(1, requests);
    }

    [Fact]
    public void RestartError_DefaultsEmpty_AndCanBeSetByComposerOnLaunchFailure()
    {
        var (vm, _, _, _) = Make();
        Assert.Equal("", vm.RestartError);

        vm.RestartError = "Couldn't restart Stats: access denied.";

        Assert.Equal("Couldn't restart Stats: access denied.", vm.RestartError);
    }
}
