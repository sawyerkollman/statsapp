using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.Updates;

namespace Stats.Core.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private static readonly MetricGroup[] GroupOrder =
        { MetricGroup.Cpu, MetricGroup.Gpu, MetricGroup.Memory, MetricGroup.Storage, MetricGroup.Network, MetricGroup.Game, MetricGroup.Motherboard, MetricGroup.Cooler };

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
    /// <summary>Dashboard selection or order changed (picker, move, remove).</summary>
    public event Action? DashboardMetricsChanged;
    /// <summary>Update now was clicked. All process/file/network work for the actual download + install happens
    /// in the composition root (App.xaml.cs) — this VM only owns the banner's display state.</summary>
    public event Action<UpdateInfo>? InstallUpdateRequested;

    [ObservableProperty] private bool _isDegraded;
    [ObservableProperty] private bool _isPickerOpen;
    [ObservableProperty] private int _flyoutTabIndex;
    [ObservableProperty] private string _pickerFilter = "";
    /// <summary>Set by the composition root once the SettingsViewModel exists; bound by the Settings tab.</summary>
    [ObservableProperty] private SettingsViewModel? _settingsPanel;

    // ---- update banner ----
    private UpdateInfo? _pendingUpdate;
    [ObservableProperty] private bool _updateAvailable;
    /// <summary>Banner text — either "Stats v1.4.2 is available" or, after a failed download, an error message.</summary>
    [ObservableProperty] private string _updateNotice = "";
    [ObservableProperty] private bool _updateBusy;
    [ObservableProperty] private double _updateProgress;

    [RelayCommand] private void TogglePicker() => IsPickerOpen = !IsPickerOpen;
    [RelayCommand] private void ToggleOverlay() => OverlayToggleRequested?.Invoke();
    [RelayCommand] private void OpenPeaks() => OpenPeaksRequested?.Invoke();
    [RelayCommand] private void OpenFans() => OpenFansRequested?.Invoke();
    [RelayCommand] private void OpenSettings() { FlyoutTabIndex = 1; IsPickerOpen = true; }
    [RelayCommand] private void CollapseAll() { foreach (var s in Sections) s.IsExpanded = false; }
    [RelayCommand] private void ExpandAll() { foreach (var s in Sections) s.IsExpanded = true; }

    [RelayCommand]
    private void InstallUpdate()
    {
        if (_pendingUpdate is UpdateInfo info) InstallUpdateRequested?.Invoke(info);
    }

    [RelayCommand] private void DismissUpdate() => UpdateAvailable = false; // dismisses for the session only

    /// <summary>Called by the composition root when a startup/24h check finds a newer release.</summary>
    public void OfferUpdate(UpdateInfo info)
    {
        _pendingUpdate = info;
        UpdateNotice = $"Stats {info.TagName} is available";
        UpdateBusy = false;
        UpdateProgress = 0;
        UpdateAvailable = true;
    }

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
        foreach (var tile in Tiles) tile.Refresh();
        CoreMatrix?.Refresh();
        if (IsPickerOpen)
        {
            foreach (var item in PickerItems)
                item.CurrentText = ValueFormatter.Format(item.Definition, _store[item.Definition.Id].Current);
        }
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

    public void RemoveTile(string id)
    {
        var picker = PickerItems.FirstOrDefault(p => p.Definition.Id == id);
        if (picker is not null) picker.IsChecked = false; // handler persists + rebuilds + saves
    }

    /// <summary>Per-metric threshold override. Both null = remove override (fall back to the group rule).</summary>
    public void SetTileThresholds(string id, float? warn, float? crit)
    {
        var def = _store.Definitions.FirstOrDefault(d => d.Id == id);
        bool lowerIsWorse = def is not null && _settings.ThresholdRules.FirstOrDefault(r => r.Group == def.Group && r.Unit == def.Unit)?.LowerIsWorse == true;
        bool ordered = warn is float w && crit is float c && (lowerIsWorse ? w > c : w < c);
        if (ordered)
            _settings.ThresholdOverrides[id] = new ThresholdRule { Warn = warn!.Value, Crit = crit!.Value, LowerIsWorse = lowerIsWorse };
        else
            _settings.ThresholdOverrides.Remove(id);
        RefreshAll();
        _saveSettings();
    }

    private void AfterPrefChange()
    {
        // Kind/Size changes need new containers (template selection happens once per container), so rebuild.
        RebuildSections();
        _saveSettings();
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

        CoreMatrix = _settings.ShowCoreMatrix ? new CoreMatrixViewModel(_store, _settings) : null;
        if (CoreMatrix is { HasCores: false }) CoreMatrix = null;
        CoreMatrix?.Refresh();

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
                tile.Refresh();
                section.Tiles.Add(tile);
                Tiles.Add(tile);
            }
            Sections.Add(section);
        }
    }

    private void OnSectionExpandedChanged(string name, bool expanded)
    {
        bool changed = expanded ? _settings.CollapsedGroups.Remove(name)
                                : !_settings.CollapsedGroups.Contains(name) && Add(_settings.CollapsedGroups, name);
        if (changed) _saveSettings();

        static bool Add(List<string> list, string v) { list.Add(v); return true; }
    }

    private MetricGroup? GroupOf(string id) => _store.TryGet(id, out _) ? _store.Definitions.First(d => d.Id == id).Group : null;

    private string FriendlyName(MetricDefinition def) =>
        _settings.TilePrefs.TryGetValue(def.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : def.DisplayName;
}
