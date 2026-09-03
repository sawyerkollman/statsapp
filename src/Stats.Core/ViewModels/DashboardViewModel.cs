using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.Updates;

namespace Stats.Core.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    /// <summary>Public so <see cref="SettingsViewModel"/> can order its threshold rule grid and "Add rule…"
    /// picker the same way the dashboard groups tiles.</summary>
    public static readonly MetricGroup[] GroupOrder =
        { MetricGroup.Cpu, MetricGroup.Gpu, MetricGroup.Memory, MetricGroup.Storage, MetricGroup.Network, MetricGroup.Game, MetricGroup.Motherboard, MetricGroup.Cooler };

    /// <summary>Consecutive unhealthy reads required before the runtime sensor-failure banner is shown; a single
    /// hiccup should not alarm the user.</summary>
    private const int SensorFailureBannerThreshold = 3;

    private readonly MetricStore _store;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Dictionary<MetricGroup, string> _groupStatus = new();
    private bool _suppressPickerEvents;

    public DashboardViewModel(MetricStore store, AppSettings settings, Action saveSettings)
    {
        _store = store;
        _settings = settings;
        _saveSettings = saveSettings;

        foreach (var def in store.Definitions)
        {
            var item = new MetricPickerItem(def,
                settings.DashboardMetrics.Contains(def.Id),
                settings.OverlayMetrics.Contains(def.Id),
                FriendlyName(def));
            item.PropertyChanged += OnPickerItemChanged;
            PickerItems.Add(item);
        }
        RebuildSections();
    }

    public ObservableCollection<GroupSectionViewModel> Sections { get; } = new();
    /// <summary>All dashboard tiles, flat (same instances as in Sections), group order then user order.</summary>
    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();
    public List<MetricPickerItem> PickerItems { get; } = new();
    public CoreMatrixViewModel? CoreMatrix { get; private set; }

    public event Action? OverlayMetricsChanged;
    public event Action? OverlayToggleRequested;
    public event Action? OpenPeaksRequested;
    public event Action? OpenFansRequested;
    /// <summary>A tile's double-click or "Details…" menu item was activated, carrying the metric id. The
    /// composition root owns the single retargetable MetricDetailWindow (see App.ShowMetricDetail).</summary>
    public event Action<string>? OpenTileDetailRequested;
    /// <summary>Dashboard selection or order changed (picker, move, remove).</summary>
    public event Action? DashboardMetricsChanged;
    /// <summary>Update now was clicked. All process/file/network work for the actual download + install happens
    /// in the composition root (App.xaml.cs) — this VM only owns the banner's display state.</summary>
    public event Action<UpdateInfo>? InstallUpdateRequested;
    /// <summary>"What's new" was clicked on the update banner, carrying <see cref="UpdateInfo.ReleasePageUrl"/>.
    /// The composition root opens it with the shell and reports a failure back through
    /// <see cref="SetReleasePageError"/> — this VM never touches Process itself.</summary>
    public event Action<string>? OpenReleasePageRequested;
    /// <summary>The Settings tab was opened (gear button or tray "Settings"). The composition root re-queries the
    /// "Stats" logon Scheduled Task so the Startup checkbox reflects current OS state rather than a stale value
    /// from the last time Settings was open.</summary>
    public event Action? SettingsOpened;

    /// <summary>Startup-only degraded status (e.g. PawnIO missing at launch). Distinct from — and never cleared
    /// by — transient runtime sensor-read recovery below.</summary>
    [ObservableProperty] private bool _isDegraded;
    /// <summary>Runtime sensor-read failure banner, shown only once SensorPoller reports three or more
    /// consecutive unhealthy reads; auto-clears on the next fully healthy read. See <see cref="SetSensorHealth"/>.</summary>
    [ObservableProperty] private bool _sensorHealthWarningVisible;
    [ObservableProperty] private string _sensorHealthNotice = "";
    [ObservableProperty] private bool _isPickerOpen;
    [ObservableProperty] private int _flyoutTabIndex;
    [ObservableProperty] private string _pickerFilter = "";
    /// <summary>Set by the composition root once the SettingsViewModel exists; bound by the Settings tab.</summary>
    [ObservableProperty] private SettingsViewModel? _settingsPanel;
    /// <summary>Dashboard-wide UI scale — set by the composition root (initial value, and again on every
    /// <see cref="SettingsChange.UiScale"/>) and bound by <c>DashboardWindow</c>'s content-root LayoutTransform.</summary>
    [ObservableProperty] private double _uiScale = 1.0;

    /// <summary>Dismissible "Gaming? Add FPS…" banner: shown while at least one Game-group metric was discovered,
    /// none of them is currently on the dashboard or overlay, and the user hasn't dismissed it before. Recomputed
    /// (via <see cref="RaiseFpsHintChanged"/>) on <see cref="RebuildSections"/> and overlay selection changes —
    /// see <see cref="OnPickerItemChanged"/>.</summary>
    public bool ShowFpsHint =>
        !_settings.FpsHintDismissed
        && _store.Definitions.Any(d => d.Group == MetricGroup.Game)
        && !_store.Definitions.Where(d => d.Group == MetricGroup.Game)
            .Any(d => _settings.DashboardMetrics.Contains(d.Id) || _settings.OverlayMetrics.Contains(d.Id));

    [RelayCommand]
    private void DismissFpsHint()
    {
        _settings.FpsHintDismissed = true;
        RaiseFpsHintChanged();
        _saveSettings();
    }

    private void RaiseFpsHintChanged() => OnPropertyChanged(nameof(ShowFpsHint));

    // ---- update banner ----
    private UpdateInfo? _pendingUpdate;
    [ObservableProperty] private bool _updateAvailable;
    /// <summary>Banner text — either "Stats v1.4.2 is available" or, after a failed download, an error message.</summary>
    [ObservableProperty] private string _updateNotice = "";
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private double _updateProgress;
    /// <summary>Set from <see cref="UpdateInfo.ReleasePageUrl"/> whenever an update is offered; "" hides the
    /// "What's new" button (e.g. an old release with no html_url).</summary>
    [ObservableProperty] private string _updateReleasePageUrl = "";
    /// <summary>"" = ok; set by the composition root when opening <see cref="UpdateReleasePageUrl"/> fails.</summary>
    [ObservableProperty] private string _releasePageError = "";

    [RelayCommand] private void TogglePicker() => IsPickerOpen = !IsPickerOpen;
    [RelayCommand] private void ToggleOverlay() => OverlayToggleRequested?.Invoke();
    [RelayCommand] private void OpenPeaks() => OpenPeaksRequested?.Invoke();
    [RelayCommand] private void OpenFans() => OpenFansRequested?.Invoke();
    [RelayCommand] private void OpenSettings() { FlyoutTabIndex = 1; IsPickerOpen = true; SettingsOpened?.Invoke(); }
    [RelayCommand] private void CollapseAll() { foreach (var s in Sections) s.IsExpanded = false; }
    [RelayCommand] private void ExpandAll() { foreach (var s in Sections) s.IsExpanded = true; }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (UpdateBusy) return; // already downloading — ignore a double-click/repeat invoke race
        if (_pendingUpdate is not UpdateInfo info) return;
        UpdateBusy = true; // set synchronously, before the composition root's async download even starts
        InstallUpdateRequested?.Invoke(info);
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        if (UpdateBusy) return; // a download is in flight — Later is a no-op (also disabled in the XAML)
        UpdateAvailable = false; // dismisses for the session only
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (string.IsNullOrEmpty(UpdateReleasePageUrl)) return;
        ReleasePageError = "";
        OpenReleasePageRequested?.Invoke(UpdateReleasePageUrl);
    }

    /// <summary>Called by the composition root when a startup/24h check — or a manual "Check for updates" from
    /// Settings — finds a newer release.</summary>
    public void OfferUpdate(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateNotice = $"Stats {info.TagName} is available";
        UpdateBusy = false;
        UpdateProgress = 0;
        UpdateReleasePageUrl = info.ReleasePageUrl;
        ReleasePageError = "";
        UpdateAvailable = true;
    }

    /// <summary>Called by the composition root after a failed attempt to open <see cref="UpdateReleasePageUrl"/>
    /// with the shell; the banner and its buttons stay intact so the user can just try again.</summary>
    public void SetReleasePageError(string message) => ReleasePageError = message;

    /// <summary>Called by the composition root while the download is in progress (0..1).</summary>
    public void SetUpdateProgress(double progress)
    {
        UpdateBusy = true;
        UpdateProgress = progress;
    }

    /// <summary>Called by the composition root when the download fails; keeps the banner (with the pending
    /// update still set) so "Update now" retries.</summary>
    public void SetUpdateError(string message)
    {
        UpdateBusy = false;
        UpdateProgress = 0;
        UpdateNotice = message;
    }

    // ---- refresh ----

    public void RefreshAll()
    {
        // Built once per batch rather than once per tile (v1.8 §10 "Cheap extras") — every tile/matrix cell below
        // looks up its governing rule in O(1) instead of scanning ThresholdRules itself.
        var thresholds = ThresholdIndex.Build(_settings);
        foreach (var tile in Tiles) tile.Refresh(thresholds);
        CoreMatrix?.Refresh(thresholds);
        if (IsPickerOpen)
        {
            foreach (var item in PickerItems)
            {
                var text = ValueFormatter.Format(item.Definition, _store[item.Definition.Id].Current);
                if (item.CurrentText != text) item.CurrentText = text; // skip the no-op PropertyChanged
            }
        }
    }

    /// <summary>Nudges every tile's (and core-matrix cell's) Severity-bound Foreground to re-evaluate after a live
    /// theme switch — see MetricTileViewModel.RaiseSeverityRefresh. Called by the composition root right after
    /// ThemeManager.Apply.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var tile in Tiles) tile.RaiseSeverityRefresh();
        CoreMatrix?.RaiseSeverityRefresh();
    }

    // ---- picker ----

    public bool PickerMatches(MetricPickerItem item)
    {
        if (string.IsNullOrWhiteSpace(PickerFilter)) return true;
        var f = PickerFilter.Trim();
        return item.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || item.Definition.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase)
            || item.Definition.HardwareName.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    public void SelectAllInGroup(string pickerGroupName, bool selected)
    {
        _suppressPickerEvents = true;
        try
        {
            foreach (var item in PickerItems.Where(p => p.GroupName == pickerGroupName))
            {
                item.IsChecked = selected;
                if (selected && !_settings.DashboardMetrics.Contains(item.Definition.Id))
                    _settings.DashboardMetrics.Add(item.Definition.Id);
                else if (!selected)
                    _settings.DashboardMetrics.Remove(item.Definition.Id);
            }
        }
        finally { _suppressPickerEvents = false; }
        RebuildSections();
        DashboardMetricsChanged?.Invoke();
        _saveSettings();
    }

    private void OnPickerItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressPickerEvents || sender is not MetricPickerItem item) return;

        if (e.PropertyName == nameof(MetricPickerItem.IsChecked))
        {
            if (item.IsChecked && !_settings.DashboardMetrics.Contains(item.Definition.Id))
                _settings.DashboardMetrics.Add(item.Definition.Id);
            else if (!item.IsChecked)
                _settings.DashboardMetrics.Remove(item.Definition.Id);
            RebuildSections();
            DashboardMetricsChanged?.Invoke();
            _saveSettings();
        }
        else if (e.PropertyName == nameof(MetricPickerItem.IsOnOverlay))
        {
            if (item.IsOnOverlay && !_settings.OverlayMetrics.Contains(item.Definition.Id))
                _settings.OverlayMetrics.Add(item.Definition.Id);
            else if (!item.IsOnOverlay)
                _settings.OverlayMetrics.Remove(item.Definition.Id);
            RaiseFpsHintChanged();
            OverlayMetricsChanged?.Invoke();
            _saveSettings();
        }
    }

    // ---- tile operations ----

    public void MoveTile(string fromId, string toId)
    {
        if (fromId == toId) return;
        var list = _settings.DashboardMetrics;
        int from = list.IndexOf(fromId), to = list.IndexOf(toId);
        if (from < 0 || to < 0) return;
        if (!_store.TryGet(fromId, out _) || !_store.TryGet(toId, out _)) return;
        if (GroupOf(fromId) != GroupOf(toId)) return;
        list.RemoveAt(from);
        list.Insert(to, fromId);
        RebuildSections();
        DashboardMetricsChanged?.Invoke();
        _saveSettings();
    }

    public void SetTileKind(string id, TileKind kind) { _settings.PrefFor(id).Kind = kind; AfterPrefChange(); }
    public void SetTileSize(string id, TileSize size) { _settings.PrefFor(id).Size = size; AfterPrefChange(); }
    public void SetTileMax(string id, float? max) { _settings.PrefFor(id).Max = max is > 0 ? max : null; AfterPrefChange(); }

    public void RenameTile(string id, string? name)
    {
        _settings.PrefFor(id).Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var picker = PickerItems.FirstOrDefault(p => p.Definition.Id == id);
        if (picker is not null) picker.DisplayName = FriendlyName(picker.Definition);
        AfterPrefChange();
        DashboardMetricsChanged?.Invoke();
    }

    /// <summary>Unchecks the picker item for <paramref name="id"/> — the picker's IsChecked handler persists,
    /// rebuilds sections, and saves. Also a <see cref="RelayCommand"/> (see <see cref="RemoveTileCommand"/>) so
    /// the tile context menu (right-click, hover "⋯" button, or Shift+F10/Apps key) can bind directly to it.</summary>
    [RelayCommand]
    public void RemoveTile(string id)
    {
        var picker = PickerItems.FirstOrDefault(p => p.Definition.Id == id);
        if (picker is not null) picker.IsChecked = false; // handler persists + rebuilds + saves
    }

    /// <summary>Opens (or retargets) the detail chart window for a tile — double-click or the context menu's
    /// "Details…" item.</summary>
    public void OpenTileDetail(string id) => OpenTileDetailRequested?.Invoke(id);

    /// <summary>Per-metric threshold override. Both null = remove override (fall back to the group rule).</summary>
    /// <summary>Stores <paramref name="rule"/> verbatim as the per-metric override (including its
    /// <see cref="ThresholdRule.LowerIsWorse"/> flag — this method no longer guesses direction from a group rule
    /// that may not exist), or removes the override when <paramref name="rule"/> is null.</summary>
    public void SetTileThresholds(string id, ThresholdRule? rule)
    {
        if (rule is null) _settings.ThresholdOverrides.Remove(id);
        else _settings.ThresholdOverrides[id] = rule;
        RefreshAll();
        _saveSettings();
    }

    // ---- tile operation commands (v1.8 §7) ----
    // Thin [RelayCommand] wrappers over the methods above — behavior is identical, the parameter records just
    // pack (id, value) into a single argument so CommunityToolkit's generator can bind a command with one
    // CommandParameter. DashboardWindow's context menu, hover "⋯" button, and keyboard menu all go through
    // these; the plain methods above stay for direct callers (tests, drag-drop's MoveTile neighbour) so this
    // promotion is zero-churn for existing call sites.

    [RelayCommand] private void SetTileKindEdit(TileKindEdit edit) => SetTileKind(edit.Id, edit.Kind);
    [RelayCommand] private void SetTileSizeEdit(TileSizeEdit edit) => SetTileSize(edit.Id, edit.Size);
    [RelayCommand] private void SetTileMaxEdit(TileMaxEdit edit) => SetTileMax(edit.Id, edit.Max);
    [RelayCommand] private void RenameTileEdit(TileRenameEdit edit) => RenameTile(edit.Id, edit.Name);
    [RelayCommand] private void SetTileThresholdEdit(TileThresholdEdit edit) => SetTileThresholds(edit.Id, edit.Rule);

    private void AfterPrefChange()
    {
        // Kind/Size changes need new containers (template selection happens once per container), so rebuild.
        RebuildSections();
        _saveSettings();
    }

    // ---- runtime sensor health ----

    /// <summary>Called by the composition root on every SensorPoller.HealthChanged tick (already marshaled to the
    /// UI thread). Shows the banner only once three or more consecutive reads have failed, and compacts multiple
    /// failing backends into one line. Never touches <see cref="IsDegraded"/> — that is startup-only status.</summary>
    public void SetSensorHealth(SensorHealthState state)
    {
        if (state.IsHealthy || state.ConsecutiveFailures < SensorFailureBannerThreshold)
        {
            SensorHealthWarningVisible = false;
            SensorHealthNotice = "";
            return;
        }
        var backends = state.FailingBackends.Count > 0 ? string.Join(", ", state.FailingBackends) : "Sensors";
        SensorHealthNotice = $"Sensor reads failing since {state.FirstFailureLocalTime:HH:mm} — {backends}: {state.LatestErrorFirstLine}";
        SensorHealthWarningVisible = true;
    }

    // ---- sections ----

    /// <summary>Per-group status line (e.g. why FPS metrics are blank). Null/empty clears.</summary>
    public void SetGroupStatus(MetricGroup group, string? text)
    {
        if (string.IsNullOrEmpty(text)) _groupStatus.Remove(group); else _groupStatus[group] = text;
        var section = Sections.FirstOrDefault(s => s.Group == group);
        if (section is not null) section.StatusText = text ?? "";
    }

    public void RebuildSections()
    {
        Tiles.Clear();
        Sections.Clear();

        var thresholds = ThresholdIndex.Build(_settings);

        CoreMatrix = _settings.ShowCoreMatrix ? new CoreMatrixViewModel(_store, _settings) : null;
        if (CoreMatrix is { HasCores: false }) CoreMatrix = null;
        CoreMatrix?.Refresh(thresholds);

        var ordered = _settings.DashboardMetrics.Where(id => _store.TryGet(id, out _)).ToList();
        var defsById = _store.Definitions.ToDictionary(d => d.Id);

        foreach (var group in GroupOrder)
        {
            var ids = ordered.Where(id => defsById[id].Group == group).ToList();
            var matrix = group == MetricGroup.Cpu ? CoreMatrix : null;
            if (ids.Count == 0 && matrix is null) continue;

            var section = new GroupSectionViewModel(group, !_settings.CollapsedGroups.Contains(group.ToString()), OnSectionExpandedChanged)
            {
                CoreMatrix = matrix,
                StatusText = _groupStatus.TryGetValue(group, out var st) ? st : "",
            };
            foreach (var id in ids)
            {
                var tile = new MetricTileViewModel(defsById[id], _store[id], _settings);
                tile.Refresh(thresholds);
                section.Tiles.Add(tile);
                Tiles.Add(tile);
            }
            Sections.Add(section);
        }
        RaiseFpsHintChanged();
    }

    private void OnSectionExpandedChanged(string name, bool expanded)
    {
        bool changed = expanded ? _settings.CollapsedGroups.Remove(name)
                                : !_settings.CollapsedGroups.Contains(name) && Add(_settings.CollapsedGroups, name);
        if (changed) _saveSettings();

        static bool Add(List<string> list, string v) { list.Add(v); return true; }
    }

    /// <summary>Resolves a metric id's group, or null when unknown — public so <c>DashboardWindow</c>'s
    /// Tile_DragOver can decide the no-drop cursor without re-implementing the lookup.</summary>
    public MetricGroup? GroupOf(string id) => _store.TryGet(id, out _) ? _store.Definitions.First(d => d.Id == id).Group : null;

    private string FriendlyName(MetricDefinition def) =>
        _settings.TilePrefs.TryGetValue(def.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : def.DisplayName;
}
