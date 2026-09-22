using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.Frames;

namespace Stats.Core.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly MetricStore _store;
    private readonly AppSettings _settings;

    public OverlayViewModel(MetricStore store, AppSettings settings)
    {
        _store = store;
        _settings = settings;
        ApplyLayout();
        Rebuild();
    }

    public ObservableCollection<MetricTileViewModel> Tiles { get; } = new();
    /// <summary>Optional freeform overlay cards. Each wraps the same live tile VM as <see cref="Tiles"/>.</summary>
    public ObservableCollection<OverlayCanvasTile> CanvasTiles { get; } = new();
    public bool HasCustomCanvas => !IsFpsOnly && _settings.OverlayCanvas is not null && CanvasTiles.Count == Tiles.Count;
    public bool HasAutomaticCanvas => !HasCustomCanvas;
    public bool HasFixedNeonBorder => _settings.OverlayCanvas?.FixedNeonBorder == true;
    public double CanvasWidth => Math.Max(1, CanvasTiles.Count == 0 ? 1 : CanvasTiles.Max(t => t.X + t.Width));
    public double CanvasHeight => Math.Max(1, CanvasTiles.Count == 0 ? 1 : CanvasTiles.Max(t => t.Y + t.Height));
    [ObservableProperty] private bool _isFpsOnly;

    [ObservableProperty] private OverlayOrientation _orientation;
    [ObservableProperty] private double _fontScale = 1.0;
    /// <summary>True while the tray's "Move overlay" mode is active — session only, never persisted. Draws the
    /// dashed outline in <c>OverlayWindow</c>; the composition root owns entering/exiting it (click-through,
    /// show/activate, tray menu header).</summary>
    [ObservableProperty] private bool _isMoveMode;
    /// <summary>OverlayGraphs == Sparkline; re-read by <see cref="ApplyLayout"/>. Drives the Sparkline's
    /// Visibility in OverlayWindow.</summary>
    [ObservableProperty] private bool _showSparklines;
    /// <summary>AppSettings.OverlayStatusLine; re-read by <see cref="ApplyLayout"/>.</summary>
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [ObservableProperty] private bool _showStatusLine;
    /// <summary>Composed strip text (<see cref="OverlayStatusComposer"/>), pushed by the composition root; ""
    /// means nothing to show.</summary>
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private bool _statusIsWarning;
    [ObservableProperty] private string _frameStatusText = "";
    [ObservableProperty] private bool _frameStatusIsWarning;
    public void SetFrameStatus(string? reason, bool isWarning = false)
    {
        if (IsFpsOnly && Tiles.Count == 0) return;
        FrameStatusText = Tiles.Any(t => FrameMetrics.IsFrameMetric(t.Definition.Id)) ? reason ?? "" : "";
        FrameStatusIsWarning = FrameStatusText.Length > 0 && isWarning;
    }
    public void ToggleFpsOnly()
    {
        IsFpsOnly = !IsFpsOnly;
        Rebuild();
        if (IsFpsOnly && Tiles.Count == 0) { FrameStatusText = "Select FPS in Overlay metrics to use FPS-only mode."; FrameStatusIsWarning = false; }
    }

    /// <summary>The strip's Visibility: on in settings AND something to say.</summary>
    public bool HasStatus => ShowStatusLine && StatusText.Length > 0;

    /// <summary>Called by App (UI thread) once per refresh while the overlay is visible; null = Empty. The
    /// toolkit-generated setters compare with EqualityComparer&lt;T&gt;.Default, so a per-tick call that changes
    /// nothing raises nothing.</summary>
    public void SetStatus(OverlayStatus? status)
    {
        status ??= OverlayStatus.Empty;
        StatusText = status.Text;
        StatusIsWarning = status.IsWarning;
    }

    /// <summary>Re-read orientation/font scale/sparkline+status flags from settings (called after Settings changes).</summary>
    public void ApplyLayout()
    {
        Orientation = _settings.OverlayOrientation;
        FontScale = _settings.OverlayFontScale;
        ShowSparklines = _settings.OverlayGraphs == OverlayGraphs.Sparkline;
        ShowStatusLine = _settings.OverlayStatusLine;
    }

    public void Rebuild()
    {
        Tiles.Clear();
        CanvasTiles.Clear();
        var thresholds = ThresholdIndex.Build(_settings);
        var selected = (IsFpsOnly ? _settings.OverlayMetrics.Where(FrameMetrics.IsFrameMetric) : _settings.OverlayMetrics).ToHashSet();
        foreach (var def in _store.Definitions.Where(d => selected.Contains(d.Id)))
        {
            var tile = new MetricTileViewModel(def, _store[def.Id], _settings);
            tile.Refresh(thresholds);
            Tiles.Add(tile);
        }
        if (!IsFpsOnly && _settings.OverlayCanvas is { } canvas)
            foreach (var element in canvas.Elements)
                if (Tiles.FirstOrDefault(tile => tile.Definition.Id == element.MetricId) is { } tile)
                    CanvasTiles.Add(new OverlayCanvasTile(tile, element));
        OnPropertyChanged(nameof(HasCustomCanvas)); OnPropertyChanged(nameof(HasAutomaticCanvas)); OnPropertyChanged(nameof(HasFixedNeonBorder)); OnPropertyChanged(nameof(CanvasWidth)); OnPropertyChanged(nameof(CanvasHeight));
    }

    public void RefreshAll()
    {
        // Built once per batch, not per tile — see DashboardViewModel.RefreshAll / v1.8 §10 "Cheap extras".
        var thresholds = ThresholdIndex.Build(_settings);
        foreach (var tile in Tiles) tile.Refresh(thresholds);
    }

    /// <summary>Nudges every overlay tile's Severity-bound Foreground to re-evaluate after a live theme switch —
    /// see MetricTileViewModel.RaiseSeverityRefresh. Called by the composition root right after ThemeManager.Apply.</summary>
    public void RaiseSeverityRefresh()
    {
        foreach (var tile in Tiles) tile.RaiseSeverityRefresh();
    }
}

public sealed class OverlayCanvasTile
{
    public OverlayCanvasTile(MetricTileViewModel tile, OverlayCanvasElement element)
    { Tile = tile; X = element.X; Y = element.Y; Width = element.Width; Height = element.Height; }
    public MetricTileViewModel Tile { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
}
