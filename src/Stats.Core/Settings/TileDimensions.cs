namespace Stats.Core.Settings;

/// <summary>Pixel dimensions per <see cref="TileSize"/> — the single source of truth for tile size everywhere it
/// matters: Stats.App's <c>TileSizeToLengthConverter</c> (XAML binding) delegates here, and
/// <see cref="ViewModels.MetricTileViewModel"/>'s Width/Height and <see cref="ViewModels.DashboardViewModel"/>'s
/// seed pack / canvas-extent math (dashboard layout modes) use it directly. S 160×80, M 224×144, L 460×192
/// (L = two M columns plus their 12-unit gap: 224*2+12 = 460 — v1.8 UI-polish §2).</summary>
public static class TileDimensions
{
    public static (double Width, double Height) Of(TileSize size) => size switch
    {
        TileSize.S => (160.0, 80.0),
        TileSize.L => (460.0, 192.0),
        _ => (224.0, 144.0),
    };
}
