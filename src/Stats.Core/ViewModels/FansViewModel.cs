using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Fans;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed record FanSourceOption(string Id, string Label);

/// <summary>One checkbox entry in a channel's multi-source picker; toggling pushes the whole selection to the controller.</summary>
public sealed partial class FanSourceSelection : ObservableObject
{
    public FanSourceSelection(string id, string label, Action<FanSourceSelection> onChanged) { Id = id; Label = label; _onChanged = onChanged; }
    private readonly Action<FanSourceSelection> _onChanged;
    public string Id { get; }
    public string Label { get; }
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) => _onChanged(this);
}

/// <summary>One controllable fan row. Setters push desired state to the controller; Refresh pulls live values.</summary>
public sealed partial class FanChannelViewModel : ObservableObject
{
    private readonly FanController _controller;
    private bool _refreshing;

    public FanChannelViewModel(FanChannelView v, FanController controller, IReadOnlyList<FanSourceOption> sourceOptions)
    {
        _controller = controller;
        _refreshing = true; // SourceSelections below must not echo the just-loaded state back to the controller
        Id = v.Id;
        Device = v.Device;
        MinPercent = v.MinPercent;
        MaxPercent = v.MaxPercent;
        SourceOptions = sourceOptions;
        _name = v.Name;
        _mode = v.Mode;
        _manualPercent = v.ManualPercent;
        _sourceMetricId = v.SourceMetricId;
        SourceSelections = new ObservableCollection<FanSourceSelection>(sourceOptions.Select(o =>
            new FanSourceSelection(o.Id, o.Label, OnSelectionChanged) { IsSelected = v.SourceMetricIds.Contains(o.Id) }));
        Points = new ObservableCollection<FanPoint>(v.Points);
        Points.CollectionChanged += OnPointsChanged;
        Apply(v);
    }

    public string Id { get; }
    public string Device { get; }
    public float MinPercent { get; }
    public float MaxPercent { get; }
    public IReadOnlyList<FanSourceOption> SourceOptions { get; }
    public ObservableCollection<FanSourceSelection> SourceSelections { get; }
    public string SourceSummary => $"Sources ({SourceSelections.Count(x => x.IsSelected)})";
    public ObservableCollection<FanPoint> Points { get; }

    [ObservableProperty] private string _name;
    [ObservableProperty] private FanMode _mode;
    [ObservableProperty] private float _manualPercent;
    [ObservableProperty] private string? _sourceMetricId;
    [ObservableProperty] private string _rpmText = "—";
    [ObservableProperty] private string _percentText = "—";
    [ObservableProperty] private string _targetText = "—";
    [ObservableProperty] private string _sourceTempText = "—";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private double _liveTemp = double.NaN;
    [ObservableProperty] private double _liveTarget = double.NaN;

    public bool IsManual => Mode == FanMode.Manual;
    public bool IsCurve => Mode == FanMode.Curve;

    partial void OnNameChanged(string value) { if (!_refreshing) _controller.SetName(Id, value); }
    partial void OnModeChanged(FanMode value)
    {
        OnPropertyChanged(nameof(IsManual));
        OnPropertyChanged(nameof(IsCurve));
        if (!_refreshing) _controller.SetMode(Id, value);
    }
    partial void OnManualPercentChanged(float value) { if (!_refreshing) _controller.SetManualPercent(Id, value); }

    /// <summary>Called by any FanSourceSelection.IsSelected setter. SourceMetricId itself is a read-only mirror
    /// of the first id now (set only from Apply) — the controller push happens here instead.</summary>
    private void OnSelectionChanged(FanSourceSelection _)
    {
        if (_refreshing) return;
        _controller.SetSources(Id, SourceSelections.Where(x => x.IsSelected).Select(x => x.Id));
        OnPropertyChanged(nameof(SourceSummary));
    }

    private void OnPointsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_refreshing) return;
        _controller.TrySetPoints(Id, Points); // invalid intermediate states (e.g. during clear) are ignored
    }

    [RelayCommand]
    private void ResetCurve()
    {
        _controller.ResetCurve(Id);
        ReplacePoints(FanCurve.DefaultPoints);
    }

    /// <summary>Pull live values (and any controller-side changes, e.g. failsafe → Auto) without echoing back.</summary>
    public void Apply(FanChannelView v)
    {
        _refreshing = true;
        try
        {
            if (Name != v.Name) Name = v.Name;
            if (Mode != v.Mode) Mode = v.Mode;
            if (ManualPercent != v.ManualPercent) ManualPercent = v.ManualPercent;
            if (SourceMetricId != v.SourceMetricId) SourceMetricId = v.SourceMetricId;
            foreach (var sel in SourceSelections)
            {
                bool want = v.SourceMetricIds.Contains(sel.Id);
                if (sel.IsSelected != want) sel.IsSelected = want;
            }
            OnPropertyChanged(nameof(SourceSummary));
            if (!Points.SequenceEqual(v.Points)) ReplacePoints(v.Points);
            RpmText = v.Rpm is float r ? $"{r:F0} RPM" : "—";
            PercentText = v.Percent is float p ? $"{p:F0} %" : "—";
            TargetText = v.TargetPercent is float t ? $"{t:F0} %" : "—";
            SourceTempText = v.SourceTemp is float s ? string.Create(CultureInfo.InvariantCulture, $"{s:F1} °C") : "—";
            LiveTemp = v.SourceTemp ?? double.NaN;
            LiveTarget = v.TargetPercent ?? double.NaN;
            StatusText = v.Status switch
            {
                FanChannelStatus.Idle => v.Mode == FanMode.Auto ? "Device control" : "",
                FanChannelStatus.Active => "Active",
                FanChannelStatus.WaitingForSource => "Waiting for temperature…",
                FanChannelStatus.SourceUnavailable => "Source unavailable — device control",
                FanChannelStatus.WriteFailed => "Write failed — check other fan software",
                _ => "",
            };
        }
        finally { _refreshing = false; }
    }

    private void ReplacePoints(IReadOnlyList<FanPoint> pts)
    {
        bool was = _refreshing; _refreshing = true;
        try { Points.Clear(); foreach (var p in pts) Points.Add(p); }
        finally { _refreshing = was; }
    }
}

public sealed partial class FanDeviceGroupViewModel : ObservableObject
{
    public FanDeviceGroupViewModel(string device) => Device = device;
    public string Device { get; }
    public ObservableCollection<FanChannelViewModel> Channels { get; } = new();
}

/// <summary>Fans window: master switch + channels grouped by device.</summary>
public sealed partial class FansViewModel : ObservableObject
{
    private readonly FanController _controller;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Func<IEnumerable<string>> _processNames;
    private readonly Func<DateTime> _clock;
    private readonly GameModeSwitcher? _switcher;
    private readonly Dictionary<string, FanChannelViewModel> _byId = new();
    private readonly IReadOnlyList<FanSourceOption> _celsiusOptions;
    private readonly bool _hardwareAtStartup;
    private DateTime _lastConflictCheck = DateTime.MinValue;
    private bool _refreshingProfiles;
    private bool _clearingProfileRefs;
    public static readonly TimeSpan ConflictCheckEvery = TimeSpan.FromSeconds(5);

    /// <summary>Raised after a Game mode setting changes (enable toggle or either profile pick) so the host
    /// can re-apply frame tracing (game mode keeps the FPS reader running while enabled).</summary>
    public event Action? GameModeChanged;

    /// <param name="hardwareEnabledAtStartup">The value of <see cref="AppSettings.ReadMotherboardAndCoolers"/> the
    /// running hardware reader was actually built with. The setting only takes effect on restart, so the live
    /// value would tell a user who just changed it exactly the wrong thing. Defaults to the live value.</param>
    public FansViewModel(FanController controller, IReadOnlyList<MetricDefinition> definitions, AppSettings settings,
        Func<IEnumerable<string>>? processNames = null, Func<DateTime>? clock = null, Action? saveSettings = null,
        GameModeSwitcher? switcher = null, bool? hardwareEnabledAtStartup = null)
    {
        _controller = controller;
        _settings = settings;
        _hardwareAtStartup = hardwareEnabledAtStartup ?? settings.ReadMotherboardAndCoolers;
        _saveSettings = saveSettings ?? (() => { });
        _processNames = processNames ?? ConflictingFanSoftware.RunningProcessNames;
        _clock = clock ?? (() => DateTime.UtcNow);
        _switcher = switcher;
        _celsiusOptions = definitions
            .Where(d => d.Unit == "°C")
            .Select(d => new FanSourceOption(d.Id,
                settings.TilePrefs.TryGetValue(d.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : d.DisplayName))
            .ToList();
        foreach (var v in controller.Views())
        {
            var group = Devices.FirstOrDefault(g => g.Device == v.Device);
            if (group is null) { group = new FanDeviceGroupViewModel(v.Device); Devices.Add(group); }
            var ch = new FanChannelViewModel(v, controller, _celsiusOptions);
            group.Channels.Add(ch);
            _byId[v.Id] = ch;
        }
        _enabled = controller.Enabled;
        foreach (var name in controller.ProfileNames()) ProfileNames.Add(name);
        _gameModeEnabled = settings.GameModeEnabled;
        _gamingProfile = settings.GameModeGamingProfile;
        _desktopProfile = settings.GameModeDesktopProfile;
        _gameModeStatus = _switcher?.StatusText ?? "";
    }

    public ObservableCollection<FanDeviceGroupViewModel> Devices { get; } = new();
    public bool HasChannels => _byId.Count > 0;
    /// <summary>Why there are no controllable fans, judged against the setting the reader was BUILT with — a user
    /// who just ticked the box needs "restart", not "enable it".</summary>
    public string UnavailableText =>
        !_hardwareAtStartup && _settings.ReadMotherboardAndCoolers
            ? "Fan control unavailable — restart Stats to apply the hardware setting you changed."
        : !_hardwareAtStartup
            ? "Fan control unavailable — enable “Read motherboard fan headers and USB coolers” in Settings and restart Stats."
        : _settings.ReadMotherboardAndCoolers
            ? "Fan control unavailable — the hardware reader is not active (degraded mode) or no controllable fans were found."
            : "Fan control unavailable — restart Stats to apply the hardware setting you changed.";

    [ObservableProperty] private bool _enabled;
    partial void OnEnabledChanged(bool value) => _controller.Enabled = value;

    [ObservableProperty] private string _recoveryNotice = "";
    public bool HasRecoveryNotice => RecoveryNotice.Length > 0;
    partial void OnRecoveryNoticeChanged(string value) => OnPropertyChanged(nameof(HasRecoveryNotice));

    [ObservableProperty] private string _conflictText = "";
    public bool HasConflict => ConflictText.Length > 0;
    partial void OnConflictTextChanged(string value) => OnPropertyChanged(nameof(HasConflict));

    [RelayCommand]
    private void DismissRecoveryNotice() => RecoveryNotice = "";

    [RelayCommand]
    private void SetAllAuto()
    {
        foreach (var ch in _byId.Values) ch.Mode = FanMode.Auto;
    }

    // ---- game mode ----

    [ObservableProperty] private bool _gameModeEnabled;
    partial void OnGameModeEnabledChanged(bool value)
    {
        _settings.GameModeEnabled = value;
        _saveSettings();
        GameModeChanged?.Invoke();
        RefreshGameModeStatus();
    }

    [ObservableProperty] private string? _gamingProfile;
    partial void OnGamingProfileChanged(string? value)
    {
        if (IsComboBoxCoercion(value, _settings.GameModeGamingProfile)) return;
        _settings.GameModeGamingProfile = value;
        _saveSettings();
        GameModeChanged?.Invoke();
    }

    [ObservableProperty] private string? _desktopProfile;
    partial void OnDesktopProfileChanged(string? value)
    {
        if (IsComboBoxCoercion(value, _settings.GameModeDesktopProfile)) return;
        _settings.GameModeDesktopProfile = value;
        _saveSettings();
        GameModeChanged?.Invoke();
    }

    /// <summary>A ComboBox cannot select an item that is not in its ItemsSource, so WPF coerces SelectedItem to
    /// null and the two-way binding pushes that null back here. Persisting it would silently erase a game-mode
    /// pick that merely refers to a profile this settings file no longer has — so a null that lands on a
    /// configured-but-missing name is dropped. Deliberate clears (DeleteProfile) set _clearingProfileRefs.</summary>
    private bool IsComboBoxCoercion(string? value, string? configured) =>
        !_clearingProfileRefs && value is null && configured is string kept && !ProfileNames.Contains(kept);

    [ObservableProperty] private string _gameModeStatus = "";

    private void RefreshGameModeStatus() => GameModeStatus = _switcher?.StatusText ?? "";

    // ---- fan profiles ----

    public ObservableCollection<string> ProfileNames { get; } = new();

    /// <summary>Live — always reflects the controller, so any channel edit (including ones made through
    /// FanChannelViewModel) is picked up without a Refresh().</summary>
    public string ActiveProfileName => _controller.ActiveProfile ?? "Custom";

    [ObservableProperty] private string? _selectedProfileName;
    partial void OnSelectedProfileNameChanged(string? value)
    {
        if (_refreshingProfiles || value is null) return;
        LoadProfile(value);
    }

    [RelayCommand]
    private void SaveProfile(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        bool isNew = !ProfileNames.Contains(name);
        _controller.AddOrReplaceProfile(_controller.SnapshotProfile(name)); // under the controller's gate
        if (isNew) ProfileNames.Add(name);
        _controller.SetActiveProfile(name); // saves settings.FanProfiles + ActiveFanProfile together
        SetSelectedProfileQuietly(name);
        SyncProfileState();
    }

    [RelayCommand]
    private void LoadProfile(string name)
    {
        if (!_controller.TryGetProfile(name, out var prof)) return;
        _controller.ApplyProfile(prof!);
        SetSelectedProfileQuietly(name);
        Refresh();
    }

    [RelayCommand]
    private void DeleteProfile(string name)
    {
        if (!_controller.RemoveProfile(name)) return;
        ProfileNames.Remove(name);
        // The game-mode picks are ordinary settings, not ComboBox state: erase them here, deliberately, rather
        // than leaving a dangling name for the ComboBox to coerce away behind the user's back.
        _clearingProfileRefs = true;
        try
        {
            if (GamingProfile == name) GamingProfile = null;
            if (DesktopProfile == name) DesktopProfile = null;
        }
        finally { _clearingProfileRefs = false; }
        if (_controller.ActiveProfile == name) _controller.SetActiveProfile(null);
        else _saveSettings();
        SyncProfileState();
    }

    [RelayCommand]
    private void CreateDefaultProfiles()
    {
        string? cpuId = PreferredCelsiusId(
            id => id.StartsWith("cpu.", StringComparison.Ordinal),
            id => id.Contains("tctl", StringComparison.OrdinalIgnoreCase) || id.Contains("package", StringComparison.OrdinalIgnoreCase));
        string? gpuId = PreferredCelsiusId(
            id => id.StartsWith("gpu.", StringComparison.Ordinal),
            id => id.Contains("core", StringComparison.OrdinalIgnoreCase));
        foreach (var prof in _controller.AddProfilesIfMissing(FanController.CreateDefaultProfiles(_controller.Channels, cpuId, gpuId)))
            ProfileNames.Add(prof.Name);
        _saveSettings();
    }

    /// <summary>First °C option matching <paramref name="prefix"/> whose id matches <paramref name="preferred"/>,
    /// else the first matching <paramref name="prefix"/> at all; null if none match the prefix.</summary>
    private string? PreferredCelsiusId(Func<string, bool> prefix, Func<string, bool> preferred)
    {
        var candidates = _celsiusOptions.Where(o => prefix(o.Id)).ToList();
        return (candidates.FirstOrDefault(o => preferred(o.Id)) ?? candidates.FirstOrDefault())?.Id;
    }

    /// <summary>Refreshes the ActiveProfileName binding. The ComboBox selection is the user's, NOT a mirror of the
    /// applied profile: any channel edit clears ActiveFanProfile ("Custom"), and forcing the selection to follow
    /// would blank the dropdown a second after every edit — leaving Delete (bound to SelectedProfileName) inert
    /// and re-picking the profile the only way back, which would throw the edit away. Only a selection naming a
    /// profile that no longer exists is cleared.</summary>
    private void SyncProfileState()
    {
        OnPropertyChanged(nameof(ActiveProfileName));
        if (SelectedProfileName is not null && !ProfileNames.Contains(SelectedProfileName)) SetSelectedProfileQuietly(null);
    }

    /// <summary>Set the ComboBox selection without the setter re-entering LoadProfile.</summary>
    private void SetSelectedProfileQuietly(string? name)
    {
        _refreshingProfiles = true;
        try { SelectedProfileName = name; }
        finally { _refreshingProfiles = false; }
    }

    public void Refresh()
    {
        var now = _clock();
        if (now - _lastConflictCheck >= ConflictCheckEvery)
        {
            _lastConflictCheck = now;
            var found = ConflictingFanSoftware.Match(_processNames());
            ConflictText = found.Count == 0 ? "" : $"Detected: {string.Join(", ", found)} — two controllers fighting the same fan is unsafe. Close them before enabling fan control.";
        }
        foreach (var v in _controller.Views())
            if (_byId.TryGetValue(v.Id, out var ch)) ch.Apply(v);
        if (Enabled != _controller.Enabled) Enabled = _controller.Enabled;
        SyncProfileState();
        RefreshGameModeStatus();
    }
}
