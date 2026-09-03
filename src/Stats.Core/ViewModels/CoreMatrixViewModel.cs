using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class CoreCellViewModel : ObservableObject
{
    public int Index { get; init; }
    [ObservableProperty] private float _load01;
    [ObservableProperty] private string _loadText = "—";
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private string _tempText = "";
    [ObservableProperty] private Severity _severity;

    /// <summary>Re-raises PropertyChanged(Severity) without changing the value — see
    /// MetricTileViewModel.RaiseSeverityRefresh for why this is needed after a live theme switch.</summary>
    public void RaiseSeverityRefresh() => OnPropertyChanged(nameof(Severity));
}

/// <summary>One cell per CPU core, aggregated from LHM's "Core #n" named sensors (loads averaged across threads).</summary>
public sealed class CoreMatrixViewModel
{
    private static readonly Regex CoreIndex = new(@"Core #(\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ExactCoreName = new(@"^(?:CPU )?Core #\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private sealed class CoreSources
    {
        public List<MetricDefinition> Loads { get; } = new();
        public MetricDefinition? Clock { get; set; }
        public MetricDefinition? Temp { get; set; }
    }

    private readonly MetricStore _store;
    private readonly AppSettings _settings;
    private readonly List<(CoreCellViewModel Cell, CoreSources Src)> _map = new();

    public CoreMatrixViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;

        var byIndex = new SortedDictionary<int, CoreSources>();
        foreach (var def in store.Definitions.Where(d => d.Group == MetricGroup.Cpu))
        {
            var m = CoreIndex.Match(def.DisplayName);
            if (!m.Success) continue;
            int idx = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            if (!byIndex.TryGetValue(idx, out var src)) byIndex[idx] = src = new CoreSources();
            switch (def.Unit)
            {
                case "%": src.Loads.Add(def); break;
                case "MHz": src.Clock ??= def; break;
                case "°C":
                    if (src.Temp is null || (!ExactCoreName.IsMatch(src.Temp.DisplayName) && ExactCoreName.IsMatch(def.DisplayName)))
                        src.Temp = def;
                    break;
            }
        }
        foreach (var (idx, src) in byIndex)
        {
            if (src.Loads.Count == 0 && src.Clock is null) continue;
            var cell = new CoreCellViewModel { Index = idx };
            _map.Add((cell, src));
            Cells.Add(cell);
        }
        Columns = Math.Clamp(Cells.Count, 1, 8);
    }

    public ObservableCollection<CoreCellViewModel> Cells { get; } = new();
    public bool HasCores => Cells.Count > 0;
    public int Columns { get; }

    /// <summary><paramref name="thresholds"/>, when supplied, avoids a per-cell threshold-rule scan the same way
    /// <see cref="MetricTileViewModel.Refresh"/>'s does — see v1.8 §10 "Cheap extras". Omitting it falls back to
    /// <see cref="ThresholdEvaluator"/> directly, with identical results.</summary>
    public void Refresh(ThresholdIndex? thresholds = null)
    {
        foreach (var (cell, src) in _map)
        {
            var loads = src.Loads.Select(d => _store[d.Id].Current).Where(v => v is float f && !float.IsNaN(f)).Select(v => v!.Value).ToList();
            if (loads.Count > 0)
            {
                float avg = loads.Average();
                cell.Load01 = Math.Clamp(avg / 100f, 0f, 1f);
                cell.LoadText = string.Create(CultureInfo.InvariantCulture, $"{avg:F0}%");
            }
            else { cell.Load01 = 0f; cell.LoadText = "—"; }

            cell.ClockText = src.Clock is null ? "" : ValueFormatter.Format(src.Clock, _store[src.Clock.Id].Current);
            if (src.Temp is null) { cell.TempText = ""; cell.Severity = Severity.Normal; }
            else
            {
                var t = _store[src.Temp.Id].Current;
                cell.TempText = ValueFormatter.Format(src.Temp, t);
                cell.Severity = Evaluate(thresholds, src.Temp, t);
            }
            if (src.Temp is null && src.Loads.Count > 0)
                cell.Severity = Evaluate(thresholds, src.Loads[0], loads.Count > 0 ? loads.Average() : null);
        }
    }

    private Severity Evaluate(ThresholdIndex? thresholds, MetricDefinition def, float? value) =>
        thresholds is not null ? thresholds.Evaluate(def, value) : ThresholdEvaluator.Evaluate(def, value, _settings);

    /// <summary>Nudges every cell's Severity-bound Foreground to re-evaluate after a live theme switch — see
    /// MetricTileViewModel.RaiseSeverityRefresh.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var cell in Cells) cell.RaiseSeverityRefresh();
    }
}
