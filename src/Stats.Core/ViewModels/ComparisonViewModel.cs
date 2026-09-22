using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stats.Core.Metrics;
using Stats.Core.Recording;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class ComparisonMetricOption : ObservableObject
{
    private readonly Action<ComparisonMetricOption> _changed;
    public ComparisonMetricOption(MetricDefinition definition, bool selected, Action<ComparisonMetricOption> changed)
    { Definition = definition; _isSelected = selected; _changed = changed; }
    public MetricDefinition Definition { get; }
    public string Label => $"{Definition.DisplayName} ({Definition.Unit})";
    [ObservableProperty] private bool _isSelected;
    partial void OnIsSelectedChanged(bool value) => _changed(this);
}

public sealed partial class ComparisonSeriesViewModel : ObservableObject
{
    [ObservableProperty] private MetricSeries _series = null!;
    [ObservableProperty] private IReadOnlyList<float> _values = [];
    [ObservableProperty] private IReadOnlyList<DateTime> _timesUtc = [];
    [ObservableProperty] private IReadOnlyList<string> _yAxisLabels = [];
    [ObservableProperty] private IReadOnlyList<string> _timeAxisLabels = [];

    public ComparisonSeriesViewModel(MetricSeries series) => Update(series, null, null);
    public string Name => Series.Definition.DisplayName;
    public string Unit => Series.Definition.Unit;

    public void Update(MetricSeries series, DateTime? axisStartUtc, DateTime? axisEndUtc)
    {
        Series = series;
        Values = series.Values.Select(value => value is float finite && float.IsFinite(finite) ? finite : float.NaN).ToArray();
        TimesUtc = series.TimesUtc.Select(ToUtc).ToArray();
        var finite = Values.Where(float.IsFinite).ToArray();
        var min = finite.Length == 0 ? 0 : finite.Min();
        var max = finite.Length == 0 ? 1 : finite.Max();
        if (Math.Abs(max - min) < 0.000001f) max = min + 1; // matches HistoryChart's flat-series range
        var definition = Series.Definition;
        YAxisLabels = finite.Length == 0 ? ["—", "—", "—"]
            : [ValueFormatter.Format(definition, max), ValueFormatter.Format(definition, (min + max) / 2), ValueFormatter.Format(definition, min)];
        TimeAxisLabels = TimeLabels(axisStartUtc, axisEndUtc);
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Unit));
    }

    private static IReadOnlyList<string> TimeLabels(DateTime? start, DateTime? end)
    {
        if (start is not DateTime first || end is not DateTime last) return [];
        var middle = first + TimeSpan.FromTicks((last - first).Ticks / 2);
        return [FormatUtc(first), FormatUtc(middle), FormatUtc(last)];
    }
    private static string FormatUtc(DateTime value) => ToUtc(value).ToString("HH:mm:ss 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
    private static DateTime ToUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed partial class ComparisonViewModel : ObservableObject
{
    private const int MaxSeries = 4;
    private readonly MetricStore _store;
    private bool _syncing;

    public ComparisonViewModel(MetricStore store, AppSettings settings, IEnumerable<string>? selectedIds = null)
    {
        _store = store;
        var selected = (selectedIds ?? settings.DashboardMetrics.Take(3)).Take(MaxSeries).ToHashSet();
        foreach (var definition in store.Definitions)
            AvailableMetrics.Add(new(definition, selected.Contains(definition.Id), Changed));
        RefreshLive();
    }

    public ObservableCollection<ComparisonMetricOption> AvailableMetrics { get; } = new();
    public ObservableCollection<ComparisonSeriesViewModel> Rows { get; } = new();
    [ObservableProperty] private string _title = "Live comparison";
    [ObservableProperty] private bool _isFrozen;
    [ObservableProperty] private bool _isLive = true;
    [ObservableProperty] private DateTime? _axisStartUtc;
    [ObservableProperty] private DateTime? _axisEndUtc;
    [ObservableProperty] private DateTime? _cursorTimeUtc;
    private bool CanFreeze() => IsLive && !IsFrozen;
    private bool CanResume() => IsLive && IsFrozen;

    partial void OnIsLiveChanged(bool value)
    {
        FreezeCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }
    partial void OnIsFrozenChanged(bool value)
    {
        FreezeCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
    }

    private void Changed(ComparisonMetricOption option)
    {
        if (_syncing) return;
        if (option.IsSelected && AvailableMetrics.Count(x => x.IsSelected) > MaxSeries)
        {
            _syncing = true; option.IsSelected = false; _syncing = false;
            return;
        }
        if (IsLive && !IsFrozen) RefreshLive();
    }

    public void RefreshLive()
    {
        if (!IsLive || IsFrozen) return;
        Replace(AvailableMetrics.Where(x => x.IsSelected && _store.TryGet(x.Definition.Id, out _))
            .Select(x => _store[x.Definition.Id].CopySeries(x.Definition)));
    }

    public void SetCaptured(string title, IEnumerable<MetricSeries> series)
    {
        IsLive = false;
        IsFrozen = true;
        Title = title;
        Replace(series.Take(MaxSeries));
    }

    public void SwitchToLive()
    {
        IsLive = true;
        IsFrozen = false;
        Title = "Live comparison";
        RefreshLive();
    }

    [RelayCommand(CanExecute = nameof(CanFreeze))] private void Freeze() => IsFrozen = true;
    [RelayCommand(CanExecute = nameof(CanResume))] private void Resume() { IsFrozen = false; RefreshLive(); }

    private void Replace(IEnumerable<MetricSeries> series)
    {
        var incoming = series.Take(MaxSeries).ToArray();
        var times = incoming.SelectMany(item => item.TimesUtc).ToArray();
        AxisStartUtc = times.Length == 0 ? null : times.Min();
        AxisEndUtc = times.Length == 0 ? null : times.Max();
        if (AxisEndUtc == AxisStartUtc && AxisEndUtc is DateTime end) AxisEndUtc = end.AddTicks(1);
        var existing = Rows.ToDictionary(row => row.Series.Definition.Id);
        for (var index = 0; index < incoming.Length; index++)
        {
            var item = incoming[index];
            if (!existing.TryGetValue(item.Definition.Id, out var row))
            {
                row = new ComparisonSeriesViewModel(item);
                Rows.Insert(index, row);
            }
            else
            {
                if (Rows.IndexOf(row) != index) Rows.Move(Rows.IndexOf(row), index);
            }
            row.Update(item, AxisStartUtc, AxisEndUtc);
        }
        foreach (var row in Rows.Skip(incoming.Length).ToArray()) Rows.Remove(row);
        CursorTimeUtc = AxisEndUtc;
    }
}
