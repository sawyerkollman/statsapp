using Stats.Core.Metrics;

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

/// <summary>Portable draft-only canvas. Metric ids are intentionally replaced by explicit compatible references.</summary>
public sealed class PortableOverlayCanvas
{
    public int Version { get; set; } = 1;
    public bool FixedNeonBorder { get; set; }
    public List<PortableOverlayCanvasElement> Elements { get; set; } = new();
    public void Validate()
    {
        if (Version != 1 || Elements is null || Elements.Count is 0 or > OverlayCanvasLayout.MaxElements ||
            Elements.Any(e => e is null || !Enum.IsDefined(e.Group) || e.Unit is null || e.Unit.Length > 100 || !e.ToElement("x").IsValid()))
            throw new InvalidDataException("Invalid portable overlay canvas.");
    }
}
public sealed class PortableOverlayCanvasElement
{
    public MetricGroup Group { get; set; }
    public string Unit { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 180;
    public double Height { get; set; } = 90;
    public OverlayCanvasElement ToElement(string metricId) => new() { MetricId = metricId, X = X, Y = Y, Width = Width, Height = Height };
}
