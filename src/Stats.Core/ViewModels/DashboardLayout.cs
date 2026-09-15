namespace Stats.Core.ViewModels;

/// <summary>Pure layout constants/math shared by the Free/Snap dashboard canvas — <see cref="DashboardViewModel"/>'s
/// <c>SetTilePosition</c>/<c>SetCoreMatrixPosition</c> and its seed pack (<c>PlaceUnpositioned</c>) are the only
/// callers, but this stays a standalone static class so it can be unit-tested without a view model.</summary>
public static class DashboardLayout
{
    /// <summary>Snap grid unit in Grid layout, and the seed pack's row/column step when the dashboard is in Grid
    /// layout.</summary>
    public const double GridSize = 16;
    /// <summary>Space left between neighbouring tiles (and the core-matrix block) by the seed pack.</summary>
    public const double Gap = 12;

    /// <summary>Rounds to the nearest multiple of <see cref="GridSize"/>, ties away from zero ("half-up" — the
    /// canvas coordinates this is ever called on are already clamped non-negative, so away-from-zero and "up" are
    /// the same thing here).</summary>
    public static double Snap(double v) => Math.Round(v / GridSize, MidpointRounding.AwayFromZero) * GridSize;

    /// <summary>Never negative — a dragged or keyboard-nudged tile can't leave the canvas past the top/left edge.</summary>
    public static double Clamp(double v) => Math.Max(0, v);
}
