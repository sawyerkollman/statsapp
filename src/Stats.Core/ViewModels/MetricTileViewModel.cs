using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Settings;

namespace Stats.Core.ViewModels;

public sealed partial class MetricTileViewModel : ObservableObject
{
    private readonly MetricHistory _history;
    private readonly AppSettings _settings;
    private readonly MetricStore? _store;
    // Game-tiles sibling plumbing (owner decision E): resolved once here, in the constructor, rather than on every
    // Refresh — a Fps-role tile's siblings never change identity after the dashboard is built (RebuildSections
    // constructs a fresh MetricTileViewModel whenever the metric selection changes). Left null for every non-Fps
    // tile and for the overlay path (store == null), so RefreshFpsSummary's "missing sibling" branch covers both
    // "this metric isn't Fps" and "no store was supplied" with the same checks.
    private readonly MetricDefinition? _lowDef;
    private readonly MetricHistory? _lowHistory;
    private readonly MetricDefinition? _frameTimeDef;
    private readonly MetricHistory? _frameTimeHistory;
    // Two alternating buffers for HistoryValues (v1.8 §10 "History arrays"): once the ring buffer is full every
    // CopyTo(reuse) call writes into the *other* slot's same-length array, so HistoryValues changes reference
    // every Refresh (WPF's binding sees a change) with zero steady-state allocation. Sparkline/HistoryChart never
    // hold onto an old array past the Refresh that swaps it out — they re-read the Values DP fresh in every
    // OnRender/OnMouseMove call — so mutating the idle buffer one tick later can never corrupt what either
    // control is currently drawing or reporting on hover.
    private float[]? _historyBufferA;
    private float[]? _historyBufferB;
    private bool _nextIsBufferA = true;
    // Same alternating pattern as HistoryValues above, sized to HistogramBinning.DefaultBinCount instead of the
    // history buffer's length — HistogramBins always changes reference on every Histogram-kind Refresh.
    private int[]? _histogramBinsA;
    private int[]? _histogramBinsB;
    private bool _nextIsHistogramBinsA = true;
    // Percentile scratch (HistogramBinning.Percentile) reused across refreshes; reallocated only when
    // HistoryValues.Length changes (warm-up, or a history-window resize), never on every steady-state tick.
    private float[]? _percentileScratch;

    /// <summary>
    /// <paramref name="store"/> is optional (null keeps every existing call site — and the overlay path, which
    /// never passes one — source-compatible; see owner decision E). When it is supplied and this tile's
    /// <see cref="GameMetricRoles.RoleOf"/> is <see cref="GameMetricRole.Fps"/>, the constructor resolves this
    /// tile's 1%-low and frame-time Game-group siblings by role (never by a hard-coded id) so
    /// <see cref="RefreshFpsSummary"/> can read them every tick without a lookup.
    /// </summary>
    public MetricTileViewModel(MetricDefinition definition, MetricHistory history, AppSettings settings, MetricStore? store = null)
    {
        Definition = definition;
        _history = history;
        _settings = settings;
        _store = store;
        _displayName = definition.DisplayName;
        _unit = definition.Unit;
        GameRole = GameMetricRoles.RoleOf(definition);

        if (store is not null && GameRole == GameMetricRole.Fps)
        {
            _lowDef = GameMetricRoles.Find(store.Definitions, GameMetricRole.LowFps);
            if (_lowDef is not null && store.TryGet(_lowDef.Id, out var lowHistory)) _lowHistory = lowHistory;

            _frameTimeDef = GameMetricRoles.Find(store.Definitions, GameMetricRole.FrameTime);
            if (_frameTimeDef is not null && store.TryGet(_frameTimeDef.Id, out var frameTimeHistory)) _frameTimeHistory = frameTimeHistory;
        }
    }

    public MetricDefinition Definition { get; }
    public string GroupName => Definition.Group.ToString();
    /// <summary>Which of the three Game-group readings this tile's own metric plays — <see cref="GameMetricRole.None"/>
    /// for everything outside <see cref="MetricGroup.Game"/>. Computed once from <see cref="Definition"/>; the tile
    /// menu (Stats.App) uses it to gate the "FPS summary" kind to <see cref="GameMetricRole.Fps"/> tiles only.</summary>
    public GameMetricRole GameRole { get; }
    /// <summary>Pixel size for the Free/Snap canvas, derived from <see cref="Size"/> via <see cref="TileDimensions"/>
    /// (dashboard layout modes) — the same numbers <c>TileBorder</c>'s Width/Height bindings already draw with in
    /// every layout mode.</summary>
    public double Width => TileDimensions.Of(Size).Width;
    public double Height => TileDimensions.Of(Size).Height;

    [ObservableProperty] private string _displayName;
    [ObservableProperty] private string _unit;
    [ObservableProperty] private string _currentText = "—";
    /// <summary>Value/unit split of <see cref="CurrentText"/> (v1.8 UI-polish §4) — the tile templates bind these
    /// two instead of splitting <see cref="CurrentText"/> on a space, so the unit's smaller/secondary styling
    /// can never drift from what the formatter actually produced.</summary>
    [ObservableProperty] private string _valueText = "—";
    [ObservableProperty] private string _unitText = "";
    [ObservableProperty] private string _minMaxText = "";
    [ObservableProperty] private string _limitText = "";
    [ObservableProperty] private string _maxText = "";
    [ObservableProperty] private string _historyWindowTag = "";
    [ObservableProperty] private float[] _historyValues = Array.Empty<float>();
    /// <summary>The backing <see cref="MetricHistory"/>'s ring-buffer capacity, so Sparkline can lay samples out
    /// on <see cref="Metrics.SampleAxis"/>'s fixed axis instead of stretching across however many samples exist.</summary>
    [ObservableProperty] private int _historySampleCapacity;
    [ObservableProperty] private Severity _severity;
    [ObservableProperty] private TileKind _kind = TileKind.Sparkline;
    [ObservableProperty] private TileSize _size = TileSize.M;
    [ObservableProperty] private float? _max;
    /// <summary>Canvas position for Free/Grid dashboard layout (dashboard layout modes); set from
    /// <see cref="TilePref.X"/>/<see cref="TilePref.Y"/> in <see cref="Refresh"/> (default 0 when unset — Auto
    /// layout never reads these, and Free/Snap always has a seeded position by the time it renders). Never written
    /// here directly — <c>DashboardViewModel.SetTilePosition</c> and the seed pack own writes.</summary>
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private float _fraction01;
    /// <summary>Non-colour severity indicator shown next to the value: "" at Normal, "▲" at Warn, "‼" at Crit —
    /// see the design's accessibility floor (§11). Colour alone (Severity's brush) must never be the only cue.</summary>
    [ObservableProperty] private string _severityGlyph = "";
    /// <summary>Screen-reader label for the whole tile: "&lt;DisplayName&gt;, &lt;CurrentText&gt;, &lt;Severity&gt;"
    /// (e.g. "Tctl, 72.0 °C, Normal"), bound to AutomationProperties.Name in the tile templates.</summary>
    [ObservableProperty] private string _automationLabel = "";

    // ---- Histogram/FpsSummary outputs (game tiles) — all computed only when a store was supplied and Kind is
    // the matching new kind (see RefreshHistogram/RefreshFpsSummary); every other tile keeps these at their
    // defaults forever, at zero per-tick cost. ----

    /// <summary>12 counts, lowest-value bin first; bound as <c>IReadOnlyList&lt;int&gt;</c> by <c>HistogramBars</c>.</summary>
    [ObservableProperty] private int[] _histogramBins = Array.Empty<int>();
    /// <summary><see cref="ValueFormatter.Format(MetricDefinition, float?)"/> of the finite min of <see cref="HistoryValues"/>;
    /// "" when there is no finite sample.</summary>
    [ObservableProperty] private string _histogramMinText = "";
    /// <summary>Same as <see cref="HistogramMinText"/> for the finite max.</summary>
    [ObservableProperty] private string _histogramMaxText = "";
    /// <summary><see cref="HistogramBinning.Fraction"/> of the marker percentile within [min, max]; NaN = no marker
    /// (all-gap buffer).</summary>
    [ObservableProperty] private double _histogramMarkerFraction = double.NaN;
    /// <summary>"p99 17.6 ms" / "p1 58 fps" — the marker's rank and formatted value; "" when there is no data.</summary>
    [ObservableProperty] private string _histogramMarkerText = "";
    /// <summary>The 1%-low Game-group sibling's formatted current value (e.g. "92 fps"); "—" when the sibling
    /// definition or history is missing, or its current value is null/NaN (<see cref="ValueFormatter.Format"/>
    /// already returns "—" for that).</summary>
    [ObservableProperty] private string _fpsLowText = "—";
    /// <summary>Same as <see cref="FpsLowText"/> for the frame-time Game-group sibling (e.g. "6.9 ms").</summary>
    [ObservableProperty] private string _fpsFrameTimeText = "—";
    /// <summary>The 1% low's own governing threshold rule (never this tile's own rule) — so the ratio bar/text
    /// colour goes amber/red on stutter even while the average is fine. <see cref="Severity.Normal"/> when there is
    /// no 1%-low sibling.</summary>
    [ObservableProperty] private Severity _fpsLowSeverity;
    /// <summary>1%-low ÷ average, clamped to [0, 1]; 0 when either is unavailable.</summary>
    [ObservableProperty] private float _fpsLowRatio;
    /// <summary>"64% of avg"; "" under the same condition as <see cref="FpsLowRatio"/> staying 0.</summary>
    [ObservableProperty] private string _fpsRatioText = "";

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
        X = pref?.X ?? 0;
        Y = pref?.Y ?? 0;

        float? limit = _settings.MetricLimits.TryGetValue(Definition.Id, out var l) && l > 0 ? l : null;
        float? explicitMax = pref?.Max is float pm && pm > 0 ? pm : limit;
        Kind = ResolveKind(pref?.Kind ?? TileKind.Auto, explicitMax);
        Max = explicitMax
              ?? (Definition.Unit == "%" ? 100f
              : !float.IsNaN(_history.SessionMax) && _history.SessionMax > 0 ? _history.SessionMax
              : null);

        var current = _history.Current;
        CurrentText = ValueFormatter.Format(Definition, current);
        (ValueText, UnitText) = ValueFormatter.FormatParts(Definition, current);
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
        if (_store is not null)
        {
            if (Kind == TileKind.Histogram) RefreshHistogram(thresholds);
            else if (Kind == TileKind.FpsSummary) RefreshFpsSummary(thresholds);
        }
        HistorySampleCapacity = _history.Capacity;
        // The requested window can be clamped (HistoryCapacity.Compute) to fit the [30, 3600]-sample buffer, so
        // the tag reports what the buffer actually covers — capacity × current poll interval — not the request.
        HistoryWindowTag = HistoryCapacity.FormatWindow(_history.Capacity * _settings.PollIntervalSeconds);

        SeverityGlyph = Severity switch { Severity.Crit => "‼", Severity.Warn => "▲", _ => "" };
        var automationLabel = $"{DisplayName}, {CurrentText}, {Severity}";
        if (Kind == TileKind.Histogram && HistogramMarkerText.Length > 0)
        {
            automationLabel = $"{automationLabel}, {HistogramMarkerText}";
        }
        else if (Kind == TileKind.FpsSummary)
        {
            automationLabel = $"{automationLabel}, 1% low {FpsLowText}, frame time {FpsFrameTimeText}";
            if (FpsRatioText.Length > 0) automationLabel = $"{automationLabel}, {FpsRatioText}";
        }
        AutomationLabel = automationLabel;
    }

    /// <summary>Re-raises PropertyChanged(Severity) (and, for the FpsSummary ratio bar, FpsLowSeverity) without
    /// changing either value — used after a live theme switch (Stats.App's ThemeManager.Apply replaces brush
    /// entries rather than mutating them) so the Foreground/Stroke/Fill Bindings that route through
    /// SeverityToBrushConverter re-evaluate and pick up the new brush instance. Called from the composition root
    /// (App), never from Core itself, which stays WPF-free.</summary>
    public void RaiseSeverityRefresh()
    {
        OnPropertyChanged(nameof(Severity));
        OnPropertyChanged(nameof(FpsLowSeverity));
    }

    /// <summary>Width/Height are derived from Size, not independently observable properties — re-raise them
    /// whenever Size changes (tile-menu resize, or Refresh above) so the Free/Snap canvas item's bound
    /// Width/Height pick up the new size immediately.</summary>
    partial void OnSizeChanged(TileSize value)
    {
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

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

    /// <summary>Same alternating-buffer pattern as <see cref="NextHistoryBuffer"/>, sized to
    /// <see cref="HistogramBinning.DefaultBinCount"/> instead of the history length (that count never changes),
    /// so it always reuses one of the same two arrays once warm.</summary>
    private int[] NextHistogramBinsBuffer()
    {
        var reuse = _nextIsHistogramBinsA ? _histogramBinsA : _histogramBinsB;
        var buffer = reuse is not null && reuse.Length == HistogramBinning.DefaultBinCount
            ? reuse
            : new int[HistogramBinning.DefaultBinCount];
        if (_nextIsHistogramBinsA) _histogramBinsA = buffer; else _histogramBinsB = buffer;
        _nextIsHistogramBinsA = !_nextIsHistogramBinsA;
        return buffer;
    }

    private TileKind ResolveKind(TileKind preferred, float? explicitMax)
    {
        if (preferred == TileKind.FpsSummary) return GameRole == GameMetricRole.Fps ? TileKind.FpsSummary : TileKind.Sparkline;
        if (preferred != TileKind.Auto) return preferred;
        bool loadPercent = Definition.Unit == "%" && Definition.Group is MetricGroup.Cpu or MetricGroup.Gpu;
        return loadPercent || explicitMax is not null ? TileKind.Gauge : TileKind.Sparkline;
    }

    /// <summary>Bins <see cref="HistoryValues"/> into <see cref="HistogramBins"/> and computes the marker (owner
    /// decision D: p99 for a metric whose governing rule is not <see cref="ThresholdRule.LowerIsWorse"/>, p1 —
    /// labelled "p1" — when it is). All-gap buffer → bins stay zero, both range texts and the marker text go to
    /// "", the marker fraction goes to NaN (<see cref="HistogramBinning.Bin"/>/<see cref="HistogramBinning.Fraction"/>
    /// already produce those values; this only skips the marker/text formatting that would otherwise format NaN).</summary>
    private void RefreshHistogram(ThresholdIndex? thresholds)
    {
        var samples = HistoryValues;
        var bins = NextHistogramBinsBuffer();
        int finite = HistogramBinning.Bin(samples, bins, out var min, out var max);
        HistogramBins = bins;

        if (finite == 0)
        {
            HistogramMinText = "";
            HistogramMaxText = "";
            HistogramMarkerFraction = double.NaN;
            HistogramMarkerText = "";
            return;
        }

        HistogramMinText = ValueFormatter.Format(Definition, min);
        HistogramMaxText = ValueFormatter.Format(Definition, max);

        var rule = thresholds?.RuleFor(Definition) ?? ThresholdEvaluator.RuleFor(Definition, _settings);
        bool lowerIsWorse = rule?.LowerIsWorse == true;
        double p = lowerIsWorse ? 0.01 : 0.99;

        if (_percentileScratch is null || _percentileScratch.Length != samples.Length)
            _percentileScratch = new float[samples.Length];
        float markerValue = HistogramBinning.Percentile(samples, p, _percentileScratch);

        HistogramMarkerFraction = HistogramBinning.Fraction(markerValue, min, max);
        HistogramMarkerText = $"p{(lowerIsWorse ? 1 : 99)} {ValueFormatter.Format(Definition, markerValue)}";
    }

    /// <summary>Formats the 1%-low/frame-time siblings resolved in the constructor (owner decision C: by
    /// <see cref="GameMetricRole"/>, never a hard-coded id) and the low/average ratio bar. A missing sibling
    /// definition (no store, or no matching Game-group definition) leaves its text at "—" and the ratio at 0 — the
    /// same result <see cref="ValueFormatter.Format"/> already gives for a present-but-gapped sibling, so both
    /// cases render identically.</summary>
    private void RefreshFpsSummary(ThresholdIndex? thresholds)
    {
        float? low = _lowHistory?.Current;
        FpsLowText = _lowDef is null || _lowHistory is null ? "—" : ValueFormatter.Format(_lowDef, low);
        FpsFrameTimeText = _frameTimeDef is null || _frameTimeHistory is null
            ? "—"
            : ValueFormatter.Format(_frameTimeDef, _frameTimeHistory.Current);
        FpsLowSeverity = _lowDef is null
            ? Severity.Normal
            : thresholds is not null ? thresholds.Evaluate(_lowDef, low) : ThresholdEvaluator.Evaluate(_lowDef, low, _settings);

        var avg = _history.Current;
        if (avg is float a && a > 0 && low is float l)
        {
            FpsLowRatio = Math.Clamp(l / a, 0f, 1f);
            FpsRatioText = string.Create(CultureInfo.InvariantCulture, $"{FpsLowRatio * 100:F0}% of avg");
        }
        else
        {
            FpsLowRatio = 0f;
            FpsRatioText = "";
        }
    }
}
