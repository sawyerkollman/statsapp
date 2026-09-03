using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class MetricTileViewModel : ObservableObject
{
    private readonly MetricHistory _history;
    private readonly AppSettings _settings;
    // Two alternating buffers for HistoryValues (v1.8 §10 "History arrays"): once the ring buffer is full every
    // CopyTo(reuse) call writes into the *other* slot's same-length array, so HistoryValues changes reference
    // every Refresh (WPF's binding sees a change) with zero steady-state allocation. Sparkline/HistoryChart never
    // hold onto an old array past the Refresh that swaps it out — they re-read the Values DP fresh in every
    // OnRender/OnMouseMove call — so mutating the idle buffer one tick later can never corrupt what either
    // control is currently drawing or reporting on hover.
    private float[]? _historyBufferA;
    private float[]? _historyBufferB;
    private bool _nextIsBufferA = true;

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
    /// <summary>Non-colour severity indicator shown next to the value: "" at Normal, "▲" at Warn, "‼" at Crit —
    /// see the design's accessibility floor (§11). Colour alone (Severity's brush) must never be the only cue.</summary>
    [ObservableProperty] private string _severityGlyph = "";
    /// <summary>Screen-reader label for the whole tile: "&lt;DisplayName&gt;, &lt;CurrentText&gt;, &lt;Severity&gt;"
    /// (e.g. "Tctl, 72.0 °C, Normal"), bound to AutomationProperties.Name in the tile templates.</summary>
    [ObservableProperty] private string _automationLabel = "";

    /// <summary>Recomputes every displayed field from the current <see cref="MetricHistory"/>/settings.
    /// <paramref name="thresholds"/>, when supplied, is a pre-built (Group, Unit) index the caller (typically
    /// <c>DashboardViewModel</c>/<c>OverlayViewModel</c>.RefreshAll, built once per batch — see v1.8 §10 "Cheap
    /// extras") uses instead of a per-tile <see cref="ThresholdEvaluator"/> rule scan; omitting it (as every
    /// existing direct/test caller does) falls back to that scan, with identical results.</summary>
    public void Refresh(ThresholdIndex? thresholds = null)
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
        Severity = thresholds is not null ? thresholds.Evaluate(Definition, current) : ThresholdEvaluator.Evaluate(Definition, current, _settings);
        Fraction01 = current is float c && Max is float m && m > 0 ? Math.Clamp(c / m, 0f, 1f) : 0f;
        MaxText = Max is float mx ? ValueFormatter.Format(Definition, mx) : "";
        MinMaxText = float.IsNaN(_history.SessionMin)
            ? ""
            : $"min {ValueFormatter.Format(Definition, _history.SessionMin)}   avg {ValueFormatter.Format(Definition, _history.SessionAvg)}   max {ValueFormatter.Format(Definition, _history.SessionMax)}";
        LimitText = limit is float lim && current is float cur
            ? string.Create(CultureInfo.InvariantCulture, $"{cur / lim * 100:F0}% of {ValueFormatter.Format(Definition, lim)}")
            : "";
        HistoryValues = NextHistoryBuffer();
        // The requested window can be clamped (HistoryCapacity.Compute) to fit the [30, 3600]-sample buffer, so
        // the tag reports what the buffer actually covers — capacity × current poll interval — not the request.
        HistoryWindowTag = HistoryCapacity.FormatWindow(_history.Capacity * _settings.PollIntervalSeconds);

        SeverityGlyph = Severity switch { Severity.Crit => "‼", Severity.Warn => "▲", _ => "" };
        AutomationLabel = $"{DisplayName}, {CurrentText}, {Severity}";
    }

    /// <summary>Re-raises PropertyChanged(Severity) without changing the value — used after a live theme switch
    /// (Stats.App's ThemeManager.Apply replaces brush entries rather than mutating them) so the Foreground/Stroke/
    /// Fill Bindings that route through SeverityToBrushConverter re-evaluate and pick up the new brush instance.
    /// Called from the composition root (App), never from Core itself, which stays WPF-free.</summary>
    public void RaiseSeverityRefresh() => OnPropertyChanged(nameof(Severity));

    /// <summary>Copies the current history into whichever of the two buffers is next in rotation, reusing it when
    /// its length still matches (steady state, buffer full) and allocating otherwise (warm-up, or a Resize). Never
    /// returns the same array instance on two consecutive calls, so binding HistoryValues to the result always
    /// raises PropertyChanged with a new reference.</summary>
    private float[] NextHistoryBuffer()
    {
        var reuse = _nextIsBufferA ? _historyBufferA : _historyBufferB;
        var buffer = _history.CopyTo(reuse);
        if (_nextIsBufferA) _historyBufferA = buffer; else _historyBufferB = buffer;
        _nextIsBufferA = !_nextIsBufferA;
        return buffer;
    }

    private TileKind ResolveKind(TileKind preferred, float? explicitMax)
    {
        if (preferred != TileKind.Auto) return preferred;
        bool loadPercent = Definition.Unit == "%" && Definition.Group is MetricGroup.Cpu or MetricGroup.Gpu;
        return loadPercent || explicitMax is not null ? TileKind.Gauge : TileKind.Sparkline;
    }
}
