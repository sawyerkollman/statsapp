namespace Stats.Core.Settings;

/// <summary>Optional, bounded overlay geometry. Null keeps the original automatic overlay.</summary>
public sealed class OverlayCanvasLayout
{
    public const int MaxElements = 32;
    public const double MaxX = 1920, MaxY = 1080, MinWidth = 120, MinHeight = 60, MaxWidth = 600, MaxHeight = 300;
    public List<OverlayCanvasElement> Elements { get; set; } = new();
    public bool FixedNeonBorder { get; set; }

    public OverlayCanvasLayout Clone() => new()
    {
        FixedNeonBorder = FixedNeonBorder,
        Elements = Elements.Select(e => e.Clone()).ToList(),
    };

    public bool IsValid() => Elements is { Count: <= MaxElements } && Elements.All(e => e is not null && e.IsValid()) &&
        Elements.Select(e => e.MetricId).Distinct(StringComparer.Ordinal).Count() == Elements.Count;

    /// <summary>Bad new geometry is discarded; established selections and overlay settings are never changed.</summary>
    public static OverlayCanvasLayout? Normalize(OverlayCanvasLayout? layout) => layout is not null && layout.IsValid() ? layout.Clone() : null;
}

public sealed class OverlayCanvasElement
{
    public string MetricId { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 90;
    public OverlayCanvasElement Clone() => new() { MetricId = MetricId, X = X, Y = Y, Width = Width, Height = Height };
    public bool IsValid() => !string.IsNullOrWhiteSpace(MetricId) && MetricId.Length <= 1024 && !MetricId.Any(char.IsControl) &&
        double.IsFinite(X) && X is >= 0 and <= OverlayCanvasLayout.MaxX &&
        double.IsFinite(Y) && Y is >= 0 and <= OverlayCanvasLayout.MaxY &&
        double.IsFinite(Width) && Width is >= OverlayCanvasLayout.MinWidth and <= OverlayCanvasLayout.MaxWidth &&
        double.IsFinite(Height) && Height is >= OverlayCanvasLayout.MinHeight and <= OverlayCanvasLayout.MaxHeight &&
        X + Width <= OverlayCanvasLayout.MaxX && Y + Height <= OverlayCanvasLayout.MaxY;
}
