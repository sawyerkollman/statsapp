using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

// No GameMode member: the game-mode controls live in the Fans window, which re-applies frame tracing through
// FansViewModel.GameModeChanged. A member nothing raises only invites the next feature onto a dead channel.
public enum SettingsChange { PollInterval, HistoryWindow, Thresholds, Limits, Overlay, Hotkey, CoreMatrix, Hardware, Updates, Theme }

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
    public event Action? OpenLogFolderRequested;
    /// <summary>The Startup checkbox was toggled by the user (not by <see cref="ApplyStartupState"/>). The
    /// composition root performs the actual schtasks /Create or /Delete asynchronously and reports the result
    /// back through <see cref="ApplyStartupState"/> — this view model never touches Process or AppSettings for
    /// this feature; it is live OS state, not a persisted preference.</summary>
    public event Action<bool>? StartupToggleRequested;

    public SettingsViewModel(AppSettings settings, IReadOnlyList<MetricDefinition> definitions, Action save)
    {
        _s = settings;
        _save = save;

        _pollIntervalSeconds = settings.PollIntervalSeconds;
        _historyWindowMinutes = settings.HistoryWindowMinutes;
        _cpuTempWarn = Rule(MetricGroup.Cpu, "°C").Warn; _cpuTempCrit = Rule(MetricGroup.Cpu, "°C").Crit;
        _gpuTempWarn = Rule(MetricGroup.Gpu, "°C").Warn; _gpuTempCrit = Rule(MetricGroup.Gpu, "°C").Crit;
        _loadWarn = Rule(MetricGroup.Cpu, "%").Warn;     _loadCrit = Rule(MetricGroup.Cpu, "%").Crit;
        _fpsWarn = Rule(MetricGroup.Game, "fps").Warn;   _fpsCrit = Rule(MetricGroup.Game, "fps").Crit;
        _overlayIsVertical = settings.OverlayOrientation == OverlayOrientation.Vertical;
        _overlayFontScale = settings.OverlayFontScale;
        _overlayOpacity = settings.OverlayOpacity;
        _overlayClickThrough = settings.OverlayClickThrough;
        _overlayHotkey = settings.OverlayHotkey;
        _showCoreMatrix = settings.ShowCoreMatrix;
        _readMotherboardAndCoolers = settings.ReadMotherboardAndCoolers;
        _checkForUpdatesAutomatically = settings.CheckForUpdatesAutomatically;
        _selectedThemePreset = settings.ThemePreset;
        _accentHex = settings.ThemeAccent ?? "";

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
    [ObservableProperty] private float _fpsWarn;
    [ObservableProperty] private float _fpsCrit;
    [ObservableProperty] private string _thresholdError = "";
    [ObservableProperty] private bool _overlayIsVertical;
    [ObservableProperty] private double _overlayFontScale;
    [ObservableProperty] private double _overlayOpacity;
    [ObservableProperty] private bool _overlayClickThrough;
    [ObservableProperty] private string _overlayHotkey;
    /// <summary>"" = ok; "Invalid hotkey"; "Hotkey disabled"; or "Hotkey unavailable — in use by another app" (set by App).</summary>
    [ObservableProperty] private string _hotkeyStatus = "";
    [ObservableProperty] private bool _showCoreMatrix;
    [ObservableProperty] private bool _readMotherboardAndCoolers;
    /// <summary>"" = ok; "Restart Stats to apply" after the setting above changes (set by App).</summary>
    [ObservableProperty] private string _hardwareStatus = "";
    [ObservableProperty] private bool _checkForUpdatesAutomatically;
    [ObservableProperty] private string _selectedThemePreset;
    /// <summary>"" = use the preset's own accent; otherwise a "#RRGGBB" override, two-way bound to the hex TextBox.</summary>
    [ObservableProperty] private string _accentHex;
    /// <summary>True while the AccentHex TextBox holds text that isn't "" and isn't valid #RRGGBB — red outline.</summary>
    [ObservableProperty] private bool _isAccentInvalid;
    /// <summary>"" = ok; set by App after Open log folder fails (folder create or shell-open error).</summary>
    [ObservableProperty] private string _diagnosticsError = "";
    /// <summary>Reflects the actual "Stats" logon Scheduled Task's existence, refreshed by the composition root
    /// via <see cref="ApplyStartupState"/> — never backed by a persisted AppSettings field.</summary>
    [ObservableProperty] private bool _startupEnabled;
    /// <summary>True while a query or /Create /Delete is in flight; the checkbox is disabled in the XAML while
    /// this is true.</summary>
    [ObservableProperty] private bool _startupBusy;
    /// <summary>"" = ok; set by App after a schtasks query/create/delete failure (non-zero exit code or a
    /// process-launch error).</summary>
    [ObservableProperty] private string _startupError = "";
    /// <summary>Guards <see cref="OnStartupEnabledChanged"/> while <see cref="ApplyStartupState"/> is writing the
    /// re-queried result back, so that write doesn't re-raise <see cref="StartupToggleRequested"/>.</summary>
    private bool _suppressStartupToggle;

    public IReadOnlyList<string> ThemePresetNames => ThemePresets.Names;
    public IReadOnlyList<string> AccentSwatches => ThemePresets.AccentSwatches;

    [RelayCommand] private void ResetOverlayPosition() => OverlayPositionResetRequested?.Invoke();
    [RelayCommand] private void ResetAccent() => AccentHex = "";
    [RelayCommand] private void SetAccent(string hex) => AccentHex = hex;
    [RelayCommand] private void OpenLogFolder() => OpenLogFolderRequested?.Invoke();

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
    partial void OnFpsWarnChanged(float value) => ApplyThresholds();
    partial void OnFpsCritChanged(float value) => ApplyThresholds();

    private void ApplyThresholds()
    {
        if (!_loaded) return;
        if (CpuTempWarn >= CpuTempCrit || GpuTempWarn >= GpuTempCrit || LoadWarn >= LoadCrit)
        {
            ThresholdError = "Warn must be below Crit";
            return;
        }
        if (FpsWarn <= FpsCrit) { ThresholdError = "FPS: warn must be above crit"; return; }
        ThresholdError = "";
        Upsert(MetricGroup.Cpu, "°C", CpuTempWarn, CpuTempCrit);
        Upsert(MetricGroup.Gpu, "°C", GpuTempWarn, GpuTempCrit);
        Upsert(MetricGroup.Cpu, "%", LoadWarn, LoadCrit);
        Upsert(MetricGroup.Gpu, "%", LoadWarn, LoadCrit);
        Upsert(MetricGroup.Game, "fps", FpsWarn, FpsCrit, lowerIsWorse: true);
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

    partial void OnReadMotherboardAndCoolersChanged(bool value)
    {
        if (!_loaded) return;
        _s.ReadMotherboardAndCoolers = value;
        Raise(SettingsChange.Hardware);
    }

    partial void OnCheckForUpdatesAutomaticallyChanged(bool value)
    {
        if (!_loaded) return;
        _s.CheckForUpdatesAutomatically = value;
        Raise(SettingsChange.Updates);
    }

    /// <summary>The checkbox is bound TwoWay, so a user click lands here first. Deliberately does not write to
    /// AppSettings or call <see cref="Raise"/> — Startup is live OS state; the composition root performs the
    /// actual schtasks mutation and reports the real result back through <see cref="ApplyStartupState"/>.</summary>
    partial void OnStartupEnabledChanged(bool value)
    {
        if (!_loaded || _suppressStartupToggle) return;
        StartupToggleRequested?.Invoke(value);
    }

    /// <summary>Called by the composition root after querying or mutating the "Stats" logon Scheduled Task.
    /// Setting <see cref="StartupEnabled"/> here reflects the actual re-queried task state and must not re-raise
    /// <see cref="StartupToggleRequested"/>, hence the suppression flag.</summary>
    public void ApplyStartupState(bool enabled, bool busy, string error)
    {
        _suppressStartupToggle = true;
        try { StartupEnabled = enabled; }
        finally { _suppressStartupToggle = false; }
        StartupBusy = busy;
        StartupError = error;
    }

    partial void OnSelectedThemePresetChanged(string value)
    {
        if (!_loaded) return;
        var sanitized = ThemePresets.SanitizePresetName(value);
        if (sanitized != value) { SelectedThemePreset = sanitized; return; } // re-enters with the sanitized name
        _s.ThemePreset = sanitized;
        Raise(SettingsChange.Theme);
    }

    partial void OnAccentHexChanged(string value)
    {
        if (!_loaded) return;
        if (value.Length == 0)
        {
            IsAccentInvalid = false;
            _s.ThemeAccent = null;
            Raise(SettingsChange.Theme);
            return;
        }
        var sanitized = ThemePresets.SanitizeAccentHex(value);
        if (sanitized is null) { IsAccentInvalid = true; return; } // leave the settings value alone until it's valid
        IsAccentInvalid = false;
        _s.ThemeAccent = sanitized;
        Raise(SettingsChange.Theme);
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

    private void Upsert(MetricGroup group, string unit, float warn, float crit, bool? lowerIsWorse = null)
    {
        var rule = _s.ThresholdRules.FirstOrDefault(r => r.Group == group && r.Unit == unit);
        if (rule is null) { rule = new ThresholdRule { Group = group, Unit = unit }; _s.ThresholdRules.Add(rule); }
        rule.Warn = warn;
        rule.Crit = crit;
        if (lowerIsWorse is bool low) rule.LowerIsWorse = low;
    }

    private void Raise(SettingsChange change)
    {
        _save();
        Changed?.Invoke(change);
    }
}
