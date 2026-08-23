using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class PeakRowViewModel : ObservableObject
{
    public PeakRowViewModel(MetricDefinition definition, string name) { Definition = definition; Name = name; }
    public MetricDefinition Definition { get; }
    public string Id => Definition.Id;
    public string Name { get; }
    [ObservableProperty] private string _nowText = "—";
    [ObservableProperty] private string _minText = "—";
    [ObservableProperty] private string _avgText = "—";
    [ObservableProperty] private string _maxText = "—";
    [ObservableProperty] private Severity _severity;

    /// <summary>Re-raises PropertyChanged(Severity) without changing the value — see
    /// MetricTileViewModel.RaiseSeverityRefresh for why this is needed after a live theme switch.</summary>
    public void RaiseSeverityRefresh() => OnPropertyChanged(nameof(Severity));
}

/// <summary>Session peaks table: Name | Now | Min | Avg | Max over dashboard-selected (or all) metrics.</summary>
public sealed partial class PeaksViewModel : ObservableObject
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;

    public PeaksViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        RebuildRows();
    }

    public ObservableCollection<PeakRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _includeAll;
    partial void OnIncludeAllChanged(bool value) => RebuildRows();

    [RelayCommand]
    private void ResetSession()
    {
        _store.ResetSession();
        Refresh();
    }

    /// <summary>Call when the dashboard selection/order changes.</summary>
    public void RebuildRows()
    {
        Rows.Clear();
        IEnumerable<MetricDefinition> defs = IncludeAll
            ? _store.Definitions
            : _settings.DashboardMetrics.Where(id => _store.TryGet(id, out _)).Select(id => _store.Definitions.First(d => d.Id == id));
        foreach (var def in defs)
        {
            var name = _settings.TilePrefs.TryGetValue(def.Id, out var p) && !string.IsNullOrWhiteSpace(p.Name) ? p.Name! : def.DisplayName;
            Rows.Add(new PeakRowViewModel(def, name));
        }
        Refresh();
    }

    public void Refresh()
    {
        foreach (var row in Rows)
        {
            var h = _store[row.Id];
            row.NowText = ValueFormatter.Format(row.Definition, h.Current);
            row.MinText = ValueFormatter.Format(row.Definition, float.IsNaN(h.SessionMin) ? null : h.SessionMin);
            row.AvgText = ValueFormatter.Format(row.Definition, float.IsNaN(h.SessionAvg) ? null : h.SessionAvg);
            row.MaxText = ValueFormatter.Format(row.Definition, float.IsNaN(h.SessionMax) ? null : h.SessionMax);
            row.Severity = ThresholdEvaluator.Evaluate(row.Definition, h.Current, _settings);
        }
    }

    /// <summary>Nudges every row's Severity-bound Foreground to re-evaluate after a live theme switch — see
    /// MetricTileViewModel.RaiseSeverityRefresh.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var row in Rows) row.RaiseSeverityRefresh();
    }
}
