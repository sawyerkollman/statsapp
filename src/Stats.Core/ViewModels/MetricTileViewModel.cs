using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;

namespace Stats.Core.ViewModels;

public sealed partial class MetricTileViewModel : ObservableObject
{
    private readonly MetricHistory _history;
    private readonly float? _limit;

    public MetricTileViewModel(MetricDefinition definition, MetricHistory history, float? limit = null)
    {
        Definition = definition;
        _history = history;
        _limit = limit;
    }

    public MetricDefinition Definition { get; }
    public string DisplayName => Definition.DisplayName;
    public string GroupName => Definition.Group.ToString();

    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private string _minMaxText = "";
    [ObservableProperty] private string _limitText = "";
    [ObservableProperty] private float[] _historyValues = Array.Empty<float>();

    public void Refresh()
    {
        CurrentText = ValueFormatter.Format(Definition, _history.Current);
        MinMaxText = float.IsNaN(_history.SessionMin)
            ? ""
            : $"min {ValueFormatter.Format(Definition, _history.SessionMin)}   max {ValueFormatter.Format(Definition, _history.SessionMax)}";
        LimitText = _limit is float limit && _history.Current is float current
            ? $"{current / limit * 100:F0}% of {ValueFormatter.Format(Definition, limit)}"
            : "";
        HistoryValues = _history.ToArray();
    }
}
