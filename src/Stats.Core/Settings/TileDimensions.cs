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

    /// <summary>Tile-resize-by-drag (owner decision assumed §1): the preset whose (W, H) is at the smallest
    /// Euclidean distance from the dragged (<paramref name="width"/>, <paramref name="height"/>). Ties (a drag
    /// that lands exactly between two presets) resolve to the larger preset — iterating S, M, L in that order and
    /// keeping a strictly-smaller-or-equal distance means the last (largest) preset tied for the minimum wins.
    /// NaN or negative inputs are treated as 0 (a degenerate/aborted drag reads as "shrink to S").</summary>
    public static TileSize Nearest(double width, double height)
    {
        width = double.IsNaN(width) || width < 0 ? 0 : width;
        height = double.IsNaN(height) || height < 0 ? 0 : height;

        var best = TileSize.S;
        var bestDistance = double.MaxValue;
        foreach (var candidate in new[] { TileSize.S, TileSize.M, TileSize.L })
        {
            var (w, h) = Of(candidate);
            double dw = width - w, dh = height - h;
            double distance = dw * dw + dh * dh;
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>Ctrl+Plus/Minus (owner decision assumed §7): steps <paramref name="size"/> by <paramref name="delta"/>
    /// positions across S(0)/M(1)/L(2), saturating at either end rather than wrapping — a press at L going larger
    /// (or at S going smaller) is a no-op.</summary>
    public static TileSize Step(TileSize size, int delta) => (TileSize)Math.Clamp((int)size + delta, 0, 2);
}
