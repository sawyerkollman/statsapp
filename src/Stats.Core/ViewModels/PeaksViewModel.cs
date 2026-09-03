using System.Collections.ObjectModel;
using System.Globalization;
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
    [ObservableProperty] private string _minAtText = "";
    [ObservableProperty] private string _maxAtText = "";
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

    public PeaksViewModel(MetricStore store, AppSettings settings, AlertLogViewModel? alertLog = null)
    {
        _store = store;
        _settings = settings;
        AlertLog = alertLog ?? new AlertLogViewModel();
        RebuildRows();
    }

    /// <summary>Backs the Peaks window's Alerts tab. The composition root passes its own long-lived instance
    /// (created before the window, so early alerts raised while the window has never been opened are still
    /// captured) — the parameterless fallback here only exists so tests and other callers aren't forced to wire
    /// one up.</summary>
    public AlertLogViewModel AlertLog { get; }

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
            row.MinAtText = FormatAt(h.SessionMinAtUtc);
            row.MaxAtText = FormatAt(h.SessionMaxAtUtc);
            row.Severity = ThresholdEvaluator.Evaluate(row.Definition, h.Current, _settings);
        }
    }

    private static string FormatAt(DateTime? utc) => utc is { } d ? $"at {d.ToLocalTime():HH:mm}" : "";

    /// <summary>Header row plus one tab-separated line per metric: metric, now, min, min-time, avg, max, max-time.
    /// Numbers are raw (no unit suffix) formatted with InvariantCulture, per the design spec's Copy button. Time
    /// columns use the same local time zone as MinAtText/MaxAtText but without the "at " prefix and with seconds,
    /// or "" when the session has no sample yet.</summary>
    public string ToTsv()
    {
        var lines = new List<string> { "Metric\tNow\tMin\tMin time\tAvg\tMax\tMax time" };
        foreach (var row in Rows)
        {
            var h = _store[row.Id];
            lines.Add(string.Join('\t',
                row.Name,
                FormatNumber(h.Current),
                FormatNumber(float.IsNaN(h.SessionMin) ? null : h.SessionMin),
                FormatTime(h.SessionMinAtUtc),
                FormatNumber(float.IsNaN(h.SessionAvg) ? null : h.SessionAvg),
                FormatNumber(float.IsNaN(h.SessionMax) ? null : h.SessionMax),
                FormatTime(h.SessionMaxAtUtc)));
        }
        return string.Join('\n', lines);
    }

    private static string FormatNumber(float? v) => v is { } n ? n.ToString("0.##", CultureInfo.InvariantCulture) : "";
    private static string FormatTime(DateTime? utc) => utc is { } d ? d.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) : "";

    /// <summary>Set by PeaksWindow's Copy button handler when Clipboard.SetText throws (clipboard access can fail
    /// transiently, e.g. another process holding the clipboard open) — Core stays WPF-free so the actual
    /// Clipboard call lives in the view's code-behind.</summary>
    [ObservableProperty] private string _copyError = "";
    partial void OnCopyErrorChanged(string value) => OnPropertyChanged(nameof(HasCopyError));
    public bool HasCopyError => CopyError.Length > 0;

    /// <summary>Nudges every row's Severity-bound Foreground to re-evaluate after a live theme switch — see
    /// MetricTileViewModel.RaiseSeverityRefresh.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var row in Rows) row.RaiseSeverityRefresh();
    }
}
