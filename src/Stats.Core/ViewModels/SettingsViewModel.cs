using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public enum SettingsChange { PollInterval, HistoryWindow, Thresholds, Limits, Overlay, Hotkey, CoreMatrix }

/// <summary>One editable metric limit (PPT/TDC/EDC/GPU power). Empty text = no limit.</summary>
public sealed partial class LimitItemViewModel : ObservableObject
{
    private readonly Action<LimitItemViewModel> _onChanged;

    public LimitItemViewModel(MetricDefinition definition, string valueText, Action<LimitItemViewModel> onChanged)
    {
        Definition = definition;
        _valueText = valueText;
        _onChanged = onChanged;
    }

    public MetricDefinition Definition { get; }
    public string Label => $"{Definition.DisplayName} ({Definition.Unit})";

    [ObservableProperty] private string _valueText;
    [ObservableProperty] private bool _isInvalid;

    partial void OnValueTextChanged(string value) => _onChanged(this);
}

/// <summary>Observable mirror of the editable AppSettings. Every setter writes through, saves, and raises Changed(reason).</summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly AppSettings _s;
    private readonly Action _save;
    private readonly bool _loaded;

    public event Action<SettingsChange>? Changed;
    public event Action? OverlayPositionResetRequested;

    public SettingsViewModel(AppSettings settings, IReadOnlyList<MetricDefinition> definitions, Action save)
    {
        _s = settings;
        _save = save;

        _pollIntervalSeconds = settings.PollIntervalSeconds;
        _historyWindowMinutes = settings.HistoryWindowMinutes;
        _cpuTempWarn = Rule(MetricGroup.Cpu, "°C").Warn; _cpuTempCrit = Rule(MetricGroup.Cpu, "°C").Crit;
        _gpuTempWarn = Rule(MetricGroup.Gpu, "°C").Warn; _gpuTempCrit = Rule(MetricGroup.Gpu, "°C").Crit;
        _loadWarn = Rule(MetricGroup.Cpu, "%").Warn;     _loadCrit = Rule(MetricGroup.Cpu, "%").Crit;
        _overlayIsVertical = settings.OverlayOrientation == OverlayOrientation.Vertical;
        _overlayFontScale = settings.OverlayFontScale;
        _overlayOpacity = settings.OverlayOpacity;
        _overlayClickThrough = settings.OverlayClickThrough;
        _overlayHotkey = settings.OverlayHotkey;
        _showCoreMatrix = settings.ShowCoreMatrix;

        foreach (var def in definitions.Where(IsLimitCandidate))
        {
            var text = settings.MetricLimits.TryGetValue(def.Id, out var v)
                ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";
            LimitItems.Add(new LimitItemViewModel(def, text, ApplyLimit));
        }

        _loaded = true;
    }

    public ObservableCollection<LimitItemViewModel> LimitItems { get; } = new();

    [ObservableProperty] private double _pollIntervalSeconds;
    [ObservableProperty] private int _historyWindowMinutes;
    [ObservableProperty] private float _cpuTempWarn;
    [ObservableProperty] private float _cpuTempCrit;
    [ObservableProperty] private float _gpuTempWarn;
    [ObservableProperty] private float _gpuTempCrit;
    [ObservableProperty] private float _loadWarn;
    [ObservableProperty] private float _loadCrit;
    [ObservableProperty] private string _thresholdError = "";
    [ObservableProperty] private bool _overlayIsVertical;
    [ObservableProperty] private double _overlayFontScale;
    [ObservableProperty] private double _overlayOpacity;
    [ObservableProperty] private bool _overlayClickThrough;
    [ObservableProperty] private string _overlayHotkey;
    /// <summary>"" = ok; "Invalid hotkey"; "Hotkey disabled"; or "Hotkey unavailable — in use by another app" (set by App).</summary>
    [ObservableProperty] private string _hotkeyStatus = "";
    [ObservableProperty] private bool _showCoreMatrix;

    [RelayCommand] private void ResetOverlayPosition() => OverlayPositionResetRequested?.Invoke();

    // ---- write-through ----

    partial void OnPollIntervalSecondsChanged(double value)
    {
        if (!_loaded) return;
        var clamped = Math.Clamp(value, 0.5, 5.0);
        if (clamped != value) { PollIntervalSeconds = clamped; return; } // re-enters with clamped
        _s.PollIntervalSeconds = clamped;
        Raise(SettingsChange.PollInterval);
    }

    partial void OnHistoryWindowMinutesChanged(int value)
    {
        if (!_loaded) return;
        var snapped = SettingsService.SnapHistoryMinutes(value);
        if (snapped != value) { HistoryWindowMinutes = snapped; return; } // re-enters with snapped
        _s.HistoryWindowMinutes = snapped;
        Raise(SettingsChange.HistoryWindow);
    }

    partial void OnCpuTempWarnChanged(float value) => ApplyThresholds();
    partial void OnCpuTempCritChanged(float value) => ApplyThresholds();
    partial void OnGpuTempWarnChanged(float value) => ApplyThresholds();
    partial void OnGpuTempCritChanged(float value) => ApplyThresholds();
    partial void OnLoadWarnChanged(float value) => ApplyThresholds();
    partial void OnLoadCritChanged(float value) => ApplyThresholds();

    private void ApplyThresholds()
    {
        if (!_loaded) return;
        if (CpuTempWarn >= CpuTempCrit || GpuTempWarn >= GpuTempCrit || LoadWarn >= LoadCrit)
        {
            ThresholdError = "Warn must be below Crit";
            return;
        }
        ThresholdError = "";
        Upsert(MetricGroup.Cpu, "°C", CpuTempWarn, CpuTempCrit);
        Upsert(MetricGroup.Gpu, "°C", GpuTempWarn, GpuTempCrit);
        Upsert(MetricGroup.Cpu, "%", LoadWarn, LoadCrit);
        Upsert(MetricGroup.Gpu, "%", LoadWarn, LoadCrit);
        Raise(SettingsChange.Thresholds);
    }

    partial void OnOverlayIsVerticalChanged(bool value)
    {
        if (!_loaded) return;
        _s.OverlayOrientation = value ? OverlayOrientation.Vertical : OverlayOrientation.Horizontal;
        Raise(SettingsChange.Overlay);
    }

    partial void OnOverlayFontScaleChanged(double value)
    {
        if (!_loaded) return;
        _s.OverlayFontScale = Math.Clamp(value, 0.8, 1.6);
        Raise(SettingsChange.Overlay);
    }

    partial void OnOverlayOpacityChanged(double value)
    {
        if (!_loaded) return;
        _s.OverlayOpacity = Math.Clamp(value, 0.3, 1.0);
        Raise(SettingsChange.Overlay);
    }

    partial void OnOverlayClickThroughChanged(bool value)
    {
        if (!_loaded) return;
        _s.OverlayClickThrough = value;
        Raise(SettingsChange.Overlay);
    }

    partial void OnOverlayHotkeyChanged(string value)
    {
        if (!_loaded) return;
        if (string.IsNullOrWhiteSpace(value))
        {
            _s.OverlayHotkey = "";
            HotkeyStatus = "Hotkey disabled";
            Raise(SettingsChange.Hotkey);
            return;
        }
        var parsed = HotkeyParser.Parse(value);
        if (parsed is null) { HotkeyStatus = "Invalid hotkey"; return; }
        _s.OverlayHotkey = parsed.Display;
        HotkeyStatus = "";
        Raise(SettingsChange.Hotkey);
    }

    partial void OnShowCoreMatrixChanged(bool value)
    {
        if (!_loaded) return;
        _s.ShowCoreMatrix = value;
        Raise(SettingsChange.CoreMatrix);
    }

    private void ApplyLimit(LimitItemViewModel item)
    {
        if (!_loaded) return;
        var text = item.ValueText.Trim();
        if (text.Length == 0)
        {
            item.IsInvalid = false;
            _s.MetricLimits.Remove(item.Definition.Id);
        }
        else if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0)
        {
            item.IsInvalid = false;
            _s.MetricLimits[item.Definition.Id] = v;
        }
        else
        {
            item.IsInvalid = true;
            return;
        }
        Raise(SettingsChange.Limits);
    }

    // ---- helpers ----

    private static bool IsLimitCandidate(MetricDefinition d) =>
        (d.Group == MetricGroup.Cpu && d.Unit is "W" or "A") || (d.Group == MetricGroup.Gpu && d.Unit == "W");

    private ThresholdRule Rule(MetricGroup group, string unit) =>
        _s.ThresholdRules.FirstOrDefault(r => r.Group == group && r.Unit == unit)
        ?? new ThresholdRule { Group = group, Unit = unit };

    private void Upsert(MetricGroup group, string unit, float warn, float crit)
    {
        var rule = _s.ThresholdRules.FirstOrDefault(r => r.Group == group && r.Unit == unit);
        if (rule is null) { rule = new ThresholdRule { Group = group, Unit = unit }; _s.ThresholdRules.Add(rule); }
        rule.Warn = warn;
        rule.Crit = crit;
    }

    private void Raise(SettingsChange change)
    {
        _save();
        Changed?.Invoke(change);
    }
}
