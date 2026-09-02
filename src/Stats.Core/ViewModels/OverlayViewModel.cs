using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;

    public OverlayViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        ApplyLayout();
        Rebuild();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();

    [ObservableProperty] private OverlayOrientation _orientation;
    [ObservableProperty] private double _fontScale = 1.0;
    /// <summary>True while the tray's "Move overlay" mode is active — session only, never persisted. Draws the
    /// dashed outline in <c>OverlayWindow</c>; the composition root owns entering/exiting it (click-through,
    /// show/activate, tray menu header).</summary>
    [ObservableProperty] private bool _isMoveMode;

    /// <summary>Re-read orientation/font scale from settings (called after Settings changes).</summary>
    public void ApplyLayout()
    {
        Orientation = _settings.OverlayOrientation;
        FontScale = _settings.OverlayFontScale;
    }

    public void Rebuild()
    {
        Tiles.Clear();
        var selected = _settings.OverlayMetrics.ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            var tile = new MetricTileViewModel(def, _store[def.Id], _settings);
            tile.Refresh();
            Tiles.Add(tile);
        }
    }

    public void RefreshAll()
    {
        foreach (var tile in Tiles) tile.Refresh();
    }

    /// <summary>Nudges every overlay tile's Severity-bound Foreground to re-evaluate after a live theme switch —
    /// see MetricTileViewModel.RaiseSeverityRefresh. Called by the composition root right after ThemeManager.Apply.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var tile in Tiles) tile.RaiseSeverityRefresh();
    }
}
