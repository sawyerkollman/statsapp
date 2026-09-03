using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

/// <summary>Drives MetricDetailWindow's HistoryChart: one instance is retargeted (via <see cref="SetTarget"/>)
/// across whichever tile the user opens next rather than recreated. Pure Core/testable — no WPF types.</summary>
public sealed partial class MetricDetailViewModel : ObservableObject
{
    /// <summary>Evenly spaced time-axis labels, oldest to "now".</summary>
    private const int TimeAxisLabelCount = 5;

    private MetricDefinition _definition;
    private MetricHistory _history;
    private readonly AppSettings _settings;
    // Same alternating-buffer scheme as MetricTileViewModel.HistoryValues (v1.8 §10) — safe for the same reason:
    // HistoryChart re-reads the Values DP fresh in every OnRender/OnMouseMove, never holding an old array past
    // the Refresh that swaps it out.
    private float[]? _valuesBufferA;
    private float[]? _valuesBufferB;
    private bool _nextIsBufferA = true;

    public MetricDetailViewModel(MetricDefinition definition, MetricHistory history, AppSettings settings)
    {
        _definition = definition;
        _history = history;
        _settings = settings;
        Refresh();
    }

    public MetricDefinition Definition => _definition;
    public string Unit => _definition.Unit;

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _currentText = "—";
    [ObservableProperty] private Severity _severity;
    [ObservableProperty] private string _minText = "—";
    [ObservableProperty] private string _avgText = "—";
    [ObservableProperty] private string _maxText = "—";
    [ObservableProperty] private float[] _values = Array.Empty<float>();
    [ObservableProperty] private double _secondsPerSample = 1.0;
    [ObservableProperty] private IReadOnlyList<string> _timeAxisLabels = Array.Empty<string>();
    [ObservableProperty] private IReadOnlyList<string> _yAxisLabels = Array.Empty<string>();
    [ObservableProperty] private float? _warnValue;
    [ObservableProperty] private float? _critValue;
    [ObservableProperty] private bool _lowerIsWorse;

    /// <summary>Retarget this VM to a different metric — the window is a single retargetable instance, not
    /// recreated per tile.</summary>
    public void SetTarget(MetricDefinition definition, MetricHistory history)
    {
        _definition = definition;
        _history = history;
        Refresh();
    }

    /// <summary>Re-raises PropertyChanged(Severity) without changing the value — used after a live theme switch,
    /// same as MetricTileViewModel.RaiseSeverityRefresh. Called from the composition root (App), never from Core
    /// itself.</summary>
    public void RaiseSeverityRefresh() => OnPropertyChanged(nameof(Severity));

    /// <summary>See MetricTileViewModel.NextHistoryBuffer — same alternating-buffer scheme for Values.</summary>
    private float[] NextValuesBuffer()
    {
        var reuse = _nextIsBufferA ? _valuesBufferA : _valuesBufferB;
        var buffer = _history.CopyTo(reuse);
        if (_nextIsBufferA) _valuesBufferA = buffer; else _valuesBufferB = buffer;
        _nextIsBufferA = !_nextIsBufferA;
        return buffer;
    }

    /// <summary>Re-pulls everything from the (possibly just-updated) MetricHistory. Called once at construction
    /// and again by the composition root on every dashboard refresh while the window is visible.</summary>
    public void Refresh()
    {
        Title = _definition.DisplayName;

        var current = _history.Current;
        CurrentText = ValueFormatter.Format(_definition, current);
        Severity = ThresholdEvaluator.Evaluate(_definition, current, _settings);
        MinText = ValueFormatter.Format(_definition, float.IsNaN(_history.SessionMin) ? null : _history.SessionMin);
        AvgText = ValueFormatter.Format(_definition, float.IsNaN(_history.SessionAvg) ? null : _history.SessionAvg);
        MaxText = ValueFormatter.Format(_definition, float.IsNaN(_history.SessionMax) ? null : _history.SessionMax);

        Values = NextValuesBuffer();
        SecondsPerSample = _settings.PollIntervalSeconds;

        var rule = _settings.ThresholdOverrides.TryGetValue(_definition.Id, out var o)
            ? o
            : _settings.ThresholdRules.FirstOrDefault(r => r.Group == _definition.Group && r.Unit == _definition.Unit);
        WarnValue = rule?.Warn;
        CritValue = rule?.Crit;
        LowerIsWorse = rule?.LowerIsWorse ?? false;

        TimeAxisLabels = BuildTimeAxisLabels(Values.Length, SecondsPerSample);
        YAxisLabels = BuildYAxisLabels(Values);
    }

    /// <summary>"<value> at <-Xs/now>" for the sample at <paramref name="index"/> — used by HistoryChart's hover
    /// crosshair. A gap (NaN) sample reports "—" rather than a bogus value.</summary>
    public string HoverText(int index)
    {
        if (Values.Length == 0 || index < 0 || index >= Values.Length) return "";
        float v = Values[index];
        string valueText = float.IsNaN(v) ? "—" : ValueFormatter.Format(_definition, v);
        double secondsAgo = (Values.Length - 1 - index) * SecondsPerSample;
        return $"{valueText} at {FormatWhen(secondsAgo)}";
    }

    private static string FormatWhen(double secondsAgo) =>
        secondsAgo < 0.5 ? "now" : "-" + HistoryCapacity.FormatWindow(secondsAgo);

    private static IReadOnlyList<string> BuildTimeAxisLabels(int sampleCount, double secondsPerSample)
    {
        if (sampleCount < 2) return new[] { "now" };
        double totalSeconds = (sampleCount - 1) * secondsPerSample;
        var labels = new string[TimeAxisLabelCount];
        for (int i = 0; i < TimeAxisLabelCount; i++)
        {
            double secondsAgo = totalSeconds * (1.0 - (double)i / (TimeAxisLabelCount - 1));
            labels[i] = FormatWhen(secondsAgo);
        }
        return labels;
    }

    private IReadOnlyList<string> BuildYAxisLabels(float[] values)
    {
        float min = float.NaN, max = float.NaN;
        foreach (var v in values)
        {
            if (float.IsNaN(v)) continue;
            if (float.IsNaN(min) || v < min) min = v;
            if (float.IsNaN(max) || v > max) max = v;
        }
        if (float.IsNaN(min)) return new[] { "—", "—", "—" };
        float mid = (min + max) / 2f;
        return new[]
        {
            ValueFormatter.Format(_definition, max),
            ValueFormatter.Format(_definition, mid),
            ValueFormatter.Format(_definition, min),
        };
    }
}
