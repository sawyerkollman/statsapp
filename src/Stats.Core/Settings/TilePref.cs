namespace Stats.Core.Settings;

public enum TileKind { Auto, Sparkline, Gauge, Bar, Value }
public enum TileSize { S, M, L }

/// <summary>Per-tile user preferences keyed by metric id.</summary>
public sealed class TilePref
{
    public TileKind Kind { get; set; } = TileKind.Auto;
    public TileSize Size { get; set; } = TileSize.M;
    /// <summary>Friendly display-name override; null/blank = use sensor name.</summary>
    public string? Name { get; set; }
    /// <summary>Explicit scale max for Gauge/Bar; null = derive (limit → 100 for % → session max).</summary>
    public float? Max { get; set; }
    /// <summary>Canvas position for Free/Grid dashboard layout; null = not yet placed (seeded by
    /// <see cref="ViewModels.DashboardViewModel"/>'s pack). Never touched while <see cref="AppSettings.DashboardLayoutMode"/>
    /// is Auto.</summary>
    public double? X { get; set; }
    public double? Y { get; set; }
}
