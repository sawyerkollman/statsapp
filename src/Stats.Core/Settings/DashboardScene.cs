using Stats.Core.Metrics;
namespace Stats.Core.Settings;

public sealed class DashboardScene
{
    public string Name { get; set; } = "";
    public string Caption { get; set; } = "";
    public Dictionary<string, string> SectionLabels { get; set; } = new();
    public LayoutProfile Layout { get; set; } = new();
    public OverlayOrientation OverlayOrientation { get; set; }
    public double OverlayFontScale { get; set; } = 1;
    public double OverlayOpacity { get; set; } = .85;
    public OverlayGraphs OverlayGraphs { get; set; } = OverlayGraphs.Sparkline;
    public OverlayCanvasLayout? OverlayCanvas { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Caption is null || Caption.Length > 500 ||
            !ValidLabels(SectionLabels) || Layout is null || Layout.DashboardMetrics is null || Layout.OverlayMetrics is null ||
            Layout.TilePrefs is null || Layout.CollapsedGroups is null ||
            Layout.DashboardMetrics.Count > 128 || Layout.OverlayMetrics.Count > 128 ||
            Layout.TilePrefs.Count > 1024 || !Enum.IsDefined(Layout.DashboardLayoutMode) ||
            Layout.DashboardMetrics.Concat(Layout.OverlayMetrics).Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 1024) ||
            !double.IsFinite(OverlayFontScale) || OverlayFontScale is < .8 or > 1.6 ||
            !double.IsFinite(OverlayOpacity) || OverlayOpacity is < .3 or > 1 ||
            OverlayCanvas is not null && !OverlayCanvas.IsValid() ||
            !Enum.IsDefined(OverlayOrientation) || !Enum.IsDefined(OverlayGraphs) ||
            Layout.TilePrefs.Any(p => p.Value is null || !Enum.IsDefined(p.Value.Kind) || !Enum.IsDefined(p.Value.Size) ||
                !ValidPosition(p.Value.X) || !ValidPosition(p.Value.Y)) ||
            !ValidPosition(Layout.CoreMatrixX) || !ValidPosition(Layout.CoreMatrixY))
            throw new InvalidDataException("Invalid scene: check name, layout and overlay values.");
    }
    public static bool ValidPosition(double? value) => value is null || (double.IsFinite(value.Value) && value is >= 0 and <= 100000);
    public static bool ValidLabels(Dictionary<string, string>? labels) => labels is not null && labels.Count <= Enum.GetValues<MetricGroup>().Length &&
        labels.All(p => Enum.TryParse<MetricGroup>(p.Key, out var group) && Enum.IsDefined(group) && p.Key == group.ToString() && p.Value is not null && p.Value.Length <= 80 && !p.Value.Any(char.IsControl));
    public void RestoreOverlay(AppSettings settings)
    {
        Validate();
        settings.OverlayOrientation = OverlayOrientation;
        settings.OverlayFontScale = OverlayFontScale;
        settings.OverlayOpacity = OverlayOpacity;
        settings.OverlayGraphs = OverlayGraphs;
        settings.OverlayCanvas = OverlayCanvasLayout.Normalize(OverlayCanvas);
    }
}

public sealed class PortableScene
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public Dictionary<string, string> SectionLabels { get; set; } = new();
    public DashboardLayoutMode Mode { get; set; }
    public List<PortableSceneSlot> Slots { get; set; } = new();
    public void Validate()
    {
        if (Version != 1 || !DashboardScene.ValidLabels(SectionLabels) || string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || !Enum.IsDefined(Mode) ||
            Slots is null || Slots.Count is 0 or > 128 || Slots.Any(s => s is null || !Enum.IsDefined(s.Group) ||
                s.Unit is null || s.Unit.Length > 100 || !Enum.IsDefined(s.Kind) || !Enum.IsDefined(s.Size) ||
                !DashboardScene.ValidPosition(s.X) || !DashboardScene.ValidPosition(s.Y) || (!s.Dashboard && !s.Overlay)))
            throw new InvalidDataException("Invalid portable scene.");
    }
}
public sealed class PortableSceneSlot
{
    public MetricGroup Group { get; set; }
    public string Unit { get; set; } = "";
    public TileKind Kind { get; set; }
    public TileSize Size { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool Dashboard { get; set; }
    public bool Overlay { get; set; }
}
