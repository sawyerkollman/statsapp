using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class MetricTileViewModel : ObservableObject
{
    private readonly MetricHistory _history;
    private readonly AppSettings _settings;

    public MetricTileViewModel(MetricDefinition definition, MetricHistory history, AppSettings settings)
    {
        Definition = definition;
        _history = history;
        _settings = settings;
        _displayName = definition.DisplayName;
        _unit = definition.Unit;
    }

    public MetricDefinition Definition { get; }
    public string GroupName => Definition.Group.ToString();

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _unit;
    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private string _minMaxText = "";
    [ObservableProperty] private string _limitText = "";
    [ObservableProperty] private string _maxText = "";
    [ObservableProperty] private string _historyWindowTag = "";
    [ObservableProperty] private float[] _historyValues = Array.Empty<float>();
    [ObservableProperty] private Severity _severity;
    [ObservableProperty] private TileKind _kind = TileKind.Sparkline;
    [ObservableProperty] private TileSize _size = TileSize.M;
    [ObservableProperty] private float? _max;
    [ObservableProperty] private float _fraction01;

    public void Refresh()
    {
        _settings.TilePrefs.TryGetValue(Definition.Id, out var pref);

        DisplayName = string.IsNullOrWhiteSpace(pref?.Name) ? Definition.DisplayName : pref!.Name!;
        Size = pref?.Size ?? TileSize.M;

        float? limit = _settings.MetricLimits.TryGetValue(Definition.Id, out var l) && l > 0 ? l : null;
        float? explicitMax = pref?.Max is float pm && pm > 0 ? pm : limit;
        Kind = ResolveKind(pref?.Kind ?? TileKind.Auto, explicitMax);
        Max = explicitMax
              ?? (Definition.Unit == "%" ? 100f
              : !float.IsNaN(_history.SessionMax) && _history.SessionMax > 0 ? _history.SessionMax
              : null);

        var current = _history.Current;
        CurrentText = ValueFormatter.Format(Definition, current);
        Severity = ThresholdEvaluator.Evaluate(Definition, current, _settings);
        Fraction01 = current is float c && Max is float m && m > 0 ? Math.Clamp(c / m, 0f, 1f) : 0f;
        MaxText = Max is float mx ? ValueFormatter.Format(Definition, mx) : "";
        MinMaxText = float.IsNaN(_history.SessionMin)
            ? ""
            : $"min {ValueFormatter.Format(Definition, _history.SessionMin)}   avg {ValueFormatter.Format(Definition, _history.SessionAvg)}   max {ValueFormatter.Format(Definition, _history.SessionMax)}";
        LimitText = limit is float lim && current is float cur
            ? string.Create(CultureInfo.InvariantCulture, $"{cur / lim * 100:F0}% of {ValueFormatter.Format(Definition, lim)}")
            : "";
        HistoryValues = _history.ToArray();
        HistoryWindowTag = $"{_settings.HistoryWindowMinutes}m";
    }

    private TileKind ResolveKind(TileKind preferred, float? explicitMax)
    {
        if (preferred != TileKind.Auto) return preferred;
        bool loadPercent = Definition.Unit == "%" && Definition.Group is MetricGroup.Cpu or MetricGroup.Gpu;
        return loadPercent || explicitMax is not null ? TileKind.Gauge : TileKind.Sparkline;
    }
}
