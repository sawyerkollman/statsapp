using System.Collections.ObjectModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed class OverlayViewModel
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;

    public OverlayViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        Rebuild();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();

    public void Rebuild()
    {
        Tiles.Clear();
        var selected = _settings.OverlayMetrics.ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            var tile = new MetricTileViewModel(def, _store[def.Id]);
            tile.Refresh();
            Tiles.Add(tile);
        }
    }

    public void RefreshAll()
    {
        foreach (var tile in Tiles) tile.Refresh();
    }
}
