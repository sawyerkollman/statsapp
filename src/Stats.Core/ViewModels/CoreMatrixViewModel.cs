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

    public void Refresh()
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
                cell.Severity = ThresholdEvaluator.Evaluate(src.Temp, t, _settings);
            }
            if (src.Temp is null && src.Loads.Count > 0)
                cell.Severity = ThresholdEvaluator.Evaluate(src.Loads[0], loads.Count > 0 ? loads.Average() : null, _settings);
        }
    }
}
