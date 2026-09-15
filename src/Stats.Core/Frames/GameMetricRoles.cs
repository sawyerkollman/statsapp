using Stats.Core.Metrics;

namespace Stats.Core.Frames;

/// <summary>Which of the three Game-group readings a metric definition plays, discriminated by unit and display
/// name rather than a hard-coded id — see <see cref="RoleOf"/>.</summary>
public enum GameMetricRole { None, Fps, LowFps, FrameTime }

/// <summary>Resolves a <see cref="MetricDefinition"/>'s <see cref="GameMetricRole"/> within
/// <see cref="MetricGroup.Game"/>, and finds a sibling definition by role — used by the FpsSummary tile to pull
/// its 1%-low and frame-time siblings from the <c>MetricStore</c> without ever referencing
/// <see cref="FrameMetrics.FpsId"/>/<see cref="FrameMetrics.LowId"/>/<see cref="FrameMetrics.FrameTimeId"/>
/// directly, so a future definition with the same shape (unit + naming convention) is picked up automatically.</summary>
public static class GameMetricRoles
{
    /// <summary><paramref name="def"/>.Group != <see cref="MetricGroup.Game"/> → <see cref="GameMetricRole.None"/>;
    /// Unit == "ms" → <see cref="GameMetricRole.FrameTime"/>; Unit == "fps" →
    /// <paramref name="def"/>.DisplayName.Contains("1%", <see cref="StringComparison.Ordinal"/>) ?
    /// <see cref="GameMetricRole.LowFps"/> : <see cref="GameMetricRole.Fps"/>; any other unit →
    /// <see cref="GameMetricRole.None"/>.</summary>
    public static GameMetricRole RoleOf(MetricDefinition def)
    {
        if (def.Group != MetricGroup.Game) return GameMetricRole.None;

        return def.Unit switch
        {
            "ms" => GameMetricRole.FrameTime,
            "fps" => def.DisplayName.Contains("1%", StringComparison.Ordinal) ? GameMetricRole.LowFps : GameMetricRole.Fps,
            _ => GameMetricRole.None,
        };
    }

    /// <summary>First definition (in enumeration order) whose <see cref="RoleOf"/> equals <paramref name="role"/>,
    /// or null when none match.</summary>
    public static MetricDefinition? Find(IEnumerable<MetricDefinition> definitions, GameMetricRole role)
    {
        foreach (var def in definitions)
        {
            if (RoleOf(def) == role) return def;
        }
        return null;
    }
}
