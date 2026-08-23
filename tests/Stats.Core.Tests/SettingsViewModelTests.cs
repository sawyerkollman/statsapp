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
}
