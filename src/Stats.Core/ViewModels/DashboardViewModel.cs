using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;
    private readonly Action _saveSettings;

    public DashboardViewModel(MetricStore store, AppSettings settings, Action saveSettings)
    {
        _store = store;
        _settings = settings;
        _saveSettings = saveSettings;

        foreach (var def in store.Definitions)
        {
            var item = new MetricPickerItem(def,
                settings.DashboardMetrics.Contains(def.Id),
                settings.OverlayMetrics.Contains(def.Id));
            item.PropertyChanged += OnPickerItemChanged;
            PickerItems.Add(item);
        }
        RebuildTiles();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();
    public List<MetricPickerItem> PickerItems { get; } = new();

    public event Action? OverlayMetricsChanged;
    public event Action? OverlayToggleRequested;

    [ObservableProperty] private bool _isDegraded;
    [ObservableProperty] private bool _isPickerOpen;

    [RelayCommand]
    private void TogglePicker() => IsPickerOpen = !IsPickerOpen;

    [RelayCommand]
    private void ToggleOverlay() => OverlayToggleRequested?.Invoke();

    public void RefreshAll()
    {
        foreach (var tile in Tiles) tile.Refresh();
    }

    private void OnPickerItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MetricPickerItem item) return;

        if (e.PropertyName == nameof(MetricPickerItem.IsChecked))
        {
            if (item.IsChecked && !_settings.DashboardMetrics.Contains(item.Definition.Id))
                _settings.DashboardMetrics.Add(item.Definition.Id);
            else if (!item.IsChecked)
                _settings.DashboardMetrics.Remove(item.Definition.Id);
            RebuildTiles();
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

    private void RebuildTiles()
    {
        Tiles.Clear();
        var selected = _settings.DashboardMetrics.ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            var tile = new MetricTileViewModel(def, _store[def.Id], _settings);
            tile.Refresh();
            Tiles.Add(tile);
        }
    }
}
