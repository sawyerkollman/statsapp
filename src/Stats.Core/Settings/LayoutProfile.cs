using System.Text.Json.Serialization;

namespace Stats.Core.Settings;

/// <summary>Saved dashboard/overlay arrangement. Display names and gauge maxima remain global preferences.</summary>
public sealed class LayoutProfile
{
    public string Name { get; set; } = "";
    public List<string> DashboardMetrics { get; set; } = new();
    public List<string> OverlayMetrics { get; set; } = new();
    public Dictionary<string, TilePref> TilePrefs { get; set; } = new();
    public List<string> CollapsedGroups { get; set; } = new();
    public bool ShowCoreMatrix { get; set; } = true;
    [JsonConverter(typeof(DashboardLayoutModeConverter))]
    public DashboardLayoutMode DashboardLayoutMode { get; set; } = DashboardLayoutMode.Auto;
    public double? CoreMatrixX { get; set; }
    public double? CoreMatrixY { get; set; }

    public static TilePref CloneArrangement(TilePref source) => new()
    {
        Kind = source.Kind,
        Size = source.Size,
        X = source.X,
        Y = source.Y,
    };

    public static LayoutProfile Capture(AppSettings settings, string name) => new()
    {
        Name = name,
        DashboardMetrics = settings.DashboardMetrics.ToList(),
        OverlayMetrics = settings.OverlayMetrics.ToList(),
        TilePrefs = settings.TilePrefs.ToDictionary(pair => pair.Key, pair => CloneArrangement(pair.Value)),
        CollapsedGroups = settings.CollapsedGroups.ToList(),
        ShowCoreMatrix = settings.ShowCoreMatrix,
        DashboardLayoutMode = settings.DashboardLayoutMode,
        CoreMatrixX = settings.CoreMatrixX,
        CoreMatrixY = settings.CoreMatrixY,
    };

    public void Restore(AppSettings settings)
    {
        var displayPrefs = settings.TilePrefs.ToDictionary(p => p.Key, p => (p.Value.Name, p.Value.Max));
        Replace(settings.DashboardMetrics, DashboardMetrics);
        Replace(settings.OverlayMetrics, OverlayMetrics);
        Replace(settings.CollapsedGroups, CollapsedGroups);
        settings.TilePrefs.Clear();
        foreach (var pair in TilePrefs) settings.TilePrefs[pair.Key] = CloneArrangement(pair.Value);
        foreach (var (id, display) in displayPrefs)
        {
            if (display.Name is null && display.Max is null) continue;
            var pref = settings.PrefFor(id);
            pref.Name = display.Name;
            pref.Max = display.Max;
        }
        settings.ShowCoreMatrix = ShowCoreMatrix;
        settings.DashboardLayoutMode = DashboardLayoutMode;
        settings.CoreMatrixX = CoreMatrixX;
        settings.CoreMatrixY = CoreMatrixY;
    }

    private static void Replace(List<string> target, IEnumerable<string> source)
    {
        target.Clear();
        target.AddRange(source);
    }
}
