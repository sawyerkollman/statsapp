using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.Tray;

namespace Stats.Core.ViewModels;

// No GameMode member: the game-mode controls live in the Fans window, which re-applies frame tracing through
// FansViewModel.GameModeChanged. A member nothing raises only invites the next feature onto a dead channel.
public enum SettingsChange { PollInterval, HistoryWindow, Thresholds, Limits, Overlay, Hotkey, CoreMatrix, Hardware, Updates, Theme, Alerts, Tray, UiScale }

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
    private readonly IReadOnlyList<MetricDefinition> _definitions;
    private readonly bool _loaded;

    public event Action<SettingsChange>? Changed;
    public event Action? OverlayPositionResetRequested;
    public event Action? OpenLogFolderRequested;
    /// <summary>The Startup checkbox was toggled by the user (not by <see cref="ApplyStartupState"/>). The
    /// composition root performs the actual schtasks /Create or /Delete asynchronously and reports the result
    /// back through <see cref="ApplyStartupState"/> — this view model never touches Process or AppSettings for
    /// this feature; it is live OS state, not a persisted preference.</summary>
    public event Action<bool>? StartupToggleRequested;
    /// <summary>"Check for updates" was clicked in the About section. The composition root runs
    /// <c>UpdateService.CheckAsync</c> (with its manual, explicit-failure mode) and reports the outcome back
    /// through <see cref="ApplyManualCheckResult"/>.</summary>
    public event Action? CheckForUpdatesRequested;
    /// <summary>"Restart now" was clicked in the Hardware section. The composition root starts a new instance of
    /// the running executable and only then calls the existing ExitApp() so the poller stops, fans restore, and
    /// settings flush through the normal clean-exit path.</summary>
    public event Action? RestartRequested;

    public SettingsViewModel(AppSettings settings, IReadOnlyList<MetricDefinition> definitions, Action save)
    {
        _s = settings;
        _save = save;
        _definitions = definitions;

        _pollIntervalSeconds = settings.PollIntervalSeconds;
        _historyWindowMinutes = settings.HistoryWindowMinutes;
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
        _alertsEnabled = settings.AlertsEnabled;
        _alertHoldSeconds = settings.AlertHoldSeconds;
        _alertSoundEnabled = settings.AlertSoundEnabled;
        _dashboardUiScale = settings.DashboardUiScale;

        foreach (var def in definitions.Where(IsLimitCandidate))
        {
            var text = settings.MetricLimits.TryGetValue(def.Id, out var v)
                ? v.ToString("0.##", CultureInfo.InvariantCulture) : "";
            LimitItems.Add(new LimitItemViewModel(def, text, ApplyLimit));
        }

        RebuildThresholdRuleItems();
        RefreshAddableRulePairs();

        TrayMetricOptions.Add(new TrayMetricOption(null, "Auto"));
        foreach (var def in TrayMetricSelector.Candidates(definitions))
            TrayMetricOptions.Add(new TrayMetricOption(def.Id, $"{def.Group} · {def.DisplayName} ({def.Unit})"));
        _selectedTrayMetric = TrayMetricOptions.FirstOrDefault(o => o.Id == settings.TrayMetricId) ?? TrayMetricOptions[0];

        _loaded = true;
    }

    public ObservableCollection<LimitItemViewModel> LimitItems { get; } = new();
    public ObservableCollection<ThresholdRuleItemViewModel> ThresholdRuleItems { get; } = new();
    public ObservableCollection<ThresholdRulePairOption> AddableRulePairs { get; } = new();
    public ObservableCollection<TrayMetricOption> TrayMetricOptions { get; } = new();

    [ObservableProperty] private double _pollIntervalSeconds;
    [ObservableProperty] private int _historyWindowMinutes;
    [ObservableProperty] private ThresholdRulePairOption? _selectedAddablePair;
    [ObservableProperty] private bool _hasAddableRulePairs;
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
    /// <summary>Entry-assembly product version display text (or "Development build"), set once by the
    /// composition root via <see cref="SetVersionInfo"/> — Assembly access itself stays in App.</summary>
    [ObservableProperty] private string _appVersionDisplay = "";
    /// <summary>True for a build whose first three version components are all zero — the About section neither
    /// offers "Check for updates" nor lets a click reach GitHub for such a build.</summary>
    [ObservableProperty] private bool _isDevBuild;
    /// <summary>True while a manual update check is in flight; the button is disabled in the XAML while this is
    /// true.</summary>
    [ObservableProperty] private bool _updateCheckBusy;
    /// <summary>"" = no result yet; "Up to date"; or an explicit failure message (see <see cref="UpdateCheckFailed"/>
    /// for which one). Cleared (left "") when a newer release is found — the dashboard banner (<see
    /// cref="DashboardViewModel.OfferUpdate"/>) carries that news instead of duplicating it here.</summary>
    [ObservableProperty] private string _updateCheckResult = "";
    /// <summary>True when <see cref="UpdateCheckResult"/> holds an explicit failure message rather than "Up to
    /// date" — the XAML uses this to show the text in the critical brush instead of the secondary one.</summary>
    [ObservableProperty] private bool _updateCheckFailed;
    /// <summary>"" = ok; set by App after "Restart now" fails to launch a new instance. Restart is not attempted
    /// again automatically — the user can just click again.</summary>
    [ObservableProperty] private string _restartError = "";
    [ObservableProperty] private bool _alertsEnabled;
    [ObservableProperty] private int _alertHoldSeconds;
    [ObservableProperty] private bool _alertSoundEnabled;
    /// <summary>Dashboard-wide UI scale (see <see cref="AppSettings.DashboardUiScale"/>); clamped 0.9–1.3.</summary>
    [ObservableProperty] private double _dashboardUiScale;
    /// <summary>Selected entry of <see cref="TrayMetricOptions"/>; the first entry (Id null) is "Auto".</summary>
    [ObservableProperty] private TrayMetricOption _selectedTrayMetric;

    public IReadOnlyList<string> ThemePresetNames => ThemePresets.Names;
    public IReadOnlyList<string> AccentSwatches => ThemePresets.AccentSwatches;

    [RelayCommand] private void ResetOverlayPosition() => OverlayPositionResetRequested?.Invoke();
    [RelayCommand] private void ResetAccent() => AccentHex = "";
    [RelayCommand] private void SetAccent(string hex) => AccentHex = hex;
    [RelayCommand] private void OpenLogFolder() => OpenLogFolderRequested?.Invoke();

    /// <summary>Seeds a new (Group, Unit) rule at 0/0 (inactive — see <see cref="ThresholdEvaluator.Evaluate"/> —
    /// so it colours nothing until the user fills in real values) for <see cref="SelectedAddablePair"/>. No-op
    /// when nothing is selected (the "Add rule…" row is hidden whenever <see cref="AddableRulePairs"/> is empty,
    /// but a stale selection could still reach here between a settings reload and the next refresh).</summary>
    [RelayCommand]
    private void AddRule()
    {
        if (SelectedAddablePair is not ThresholdRulePairOption pair) return;
        _s.ThresholdRules.Add(new ThresholdRule { Group = pair.Group, Unit = pair.Unit, Warn = 0, Crit = 0, LowerIsWorse = false });
        RebuildThresholdRuleItems();
        RefreshAddableRulePairs();
        Raise(SettingsChange.Thresholds);
    }

    [RelayCommand]
    private void CheckForUpdates()
    {
        if (UpdateCheckBusy || IsDevBuild) return; // dev builds never reach GitHub, even from this button
        UpdateCheckBusy = true;
        UpdateCheckResult = "";
        UpdateCheckFailed = false;
        CheckForUpdatesRequested?.Invoke();
    }

    [RelayCommand] private void RestartNow() => RestartRequested?.Invoke();

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

    partial void OnAlertsEnabledChanged(bool value)
    {
        if (!_loaded) return;
        _s.AlertsEnabled = value;
        Raise(SettingsChange.Alerts);
    }

    partial void OnAlertHoldSecondsChanged(int value)
    {
        if (!_loaded) return;
        var clamped = Math.Clamp(value, 1, 120);
        if (clamped != value) { AlertHoldSeconds = clamped; return; } // re-enters with clamped
        _s.AlertHoldSeconds = clamped;
        Raise(SettingsChange.Alerts);
    }

    partial void OnAlertSoundEnabledChanged(bool value)
    {
        if (!_loaded) return;
        _s.AlertSoundEnabled = value;
        Raise(SettingsChange.Alerts);
    }

    partial void OnSelectedTrayMetricChanged(TrayMetricOption value)
    {
        if (!_loaded) return;
        _s.TrayMetricId = value.Id;
        Raise(SettingsChange.Tray);
    }

    partial void OnDashboardUiScaleChanged(double value)
    {
        if (!_loaded) return;
        var clamped = Math.Clamp(value, 0.9, 1.3);
        if (clamped != value) { DashboardUiScale = clamped; return; } // re-enters with clamped
        _s.DashboardUiScale = clamped;
        Raise(SettingsChange.UiScale);
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

    /// <summary>Called once by the composition root, right after construction, with the entry assembly's version
    /// already formatted/classified by the pure <c>UpdateChecker</c> helpers in Core.</summary>
    public void SetVersionInfo(string appVersionDisplay, bool isDevBuild)
    {
        AppVersionDisplay = appVersionDisplay;
        IsDevBuild = isDevBuild;
    }

    /// <summary>Called by the composition root after a manual check completes: "Up to date" for no result, or an
    /// explicit failure message (<paramref name="failed"/> true). A found update is reported to the dashboard
    /// banner instead, so callers pass "" here in that case.</summary>
    public void ApplyManualCheckResult(string result, bool failed = false)
    {
        UpdateCheckBusy = false;
        UpdateCheckResult = result;
        UpdateCheckFailed = failed;
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

    /// <summary>Rebuilds <see cref="ThresholdRuleItems"/> from <see cref="AppSettings.ThresholdRules"/>, ordered by
    /// <see cref="DashboardViewModel.GroupOrder"/> then unit — the same order the dashboard groups tiles in.</summary>
    private void RebuildThresholdRuleItems()
    {
        ThresholdRuleItems.Clear();
        foreach (var rule in _s.ThresholdRules
            .OrderBy(r => Array.IndexOf(DashboardViewModel.GroupOrder, r.Group))
            .ThenBy(r => r.Unit, StringComparer.Ordinal))
        {
            ThresholdRuleItems.Add(new ThresholdRuleItemViewModel(rule, ApplyThresholdRuleItem));
        }
    }

    /// <summary>Parses and validates one row's edited <see cref="ThresholdRuleItemViewModel.WarnText"/>/
    /// <see cref="ThresholdRuleItemViewModel.CritText"/> with the same <see cref="ThresholdInput.TryParse"/> the
    /// per-tile dialog uses, against the row's (fixed) direction. A valid, ordered pair writes through to the
    /// underlying <see cref="ThresholdRule"/>, saves, and raises <see cref="SettingsChange.Thresholds"/>; anything
    /// else — unparseable text or a bad ordering — leaves the rule untouched and only sets the row's
    /// <see cref="ThresholdRuleItemViewModel.Error"/>, so bad input is never silently dropped.</summary>
    private void ApplyThresholdRuleItem(ThresholdRuleItemViewModel item)
    {
        if (!_loaded) return;
        if (!ThresholdInput.TryParse(item.WarnText, item.CritText, item.LowerIsWorse, out var parsed, out var error))
        {
            item.Error = error;
            return;
        }
        item.Error = "";
        item.Rule.Warn = parsed.Warn;
        item.Rule.Crit = parsed.Crit;
        Raise(SettingsChange.Thresholds);
    }

    /// <summary>Recomputes the (Group, Unit) pairs discovered among <see cref="_definitions"/> that have no rule
    /// yet, ordered the same way as <see cref="ThresholdRuleItems"/>, and resets <see cref="SelectedAddablePair"/>
    /// to the first (or none, hiding the "Add rule…" row via <see cref="HasAddableRulePairs"/>).</summary>
    private void RefreshAddableRulePairs()
    {
        var existing = _s.ThresholdRules.Select(r => (r.Group, r.Unit)).ToHashSet();
        var discovered = _definitions
            .Select(d => (d.Group, d.Unit))
            .Distinct()
            .Where(p => !existing.Contains(p))
            .OrderBy(p => Array.IndexOf(DashboardViewModel.GroupOrder, p.Group))
            .ThenBy(p => p.Unit, StringComparer.Ordinal);

        AddableRulePairs.Clear();
        foreach (var (group, unit) in discovered) AddableRulePairs.Add(new ThresholdRulePairOption(group, unit));
        HasAddableRulePairs = AddableRulePairs.Count > 0;
        SelectedAddablePair = AddableRulePairs.FirstOrDefault();
    }

    private void Raise(SettingsChange change)
    {
        _save();
        Changed?.Invoke(change);
    }
}

/// <summary>One entry of <see cref="SettingsViewModel.TrayMetricOptions"/>: <see cref="Id"/> null is "Auto" (App
/// falls back to its CPU-temp heuristic); otherwise a discovered °C/% metric's id. <see cref="ToString"/> is the
/// ComboBox's default display text.</summary>
public sealed record TrayMetricOption(string? Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
