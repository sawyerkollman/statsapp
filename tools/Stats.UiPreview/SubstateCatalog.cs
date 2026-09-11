namespace Stats.UiPreview;

/// <summary>Which --substate values make sense for which --view, per PREVIEW_HARNESS.md's command interface.
/// Program.cs prints a clear error for a substate that makes no sense for the requested view instead of silently
/// ignoring it.</summary>
internal static class SubstateCatalog
{
    private static readonly Dictionary<string, string[]> ByView = new()
    {
        ["dashboard"] = new[] { "collapsed-all", "tile-menu", "degraded", "sensor-failure", "update-offered", "update-progress", "update-error", "fps-hint", "theme-cycle",
            "graphs-plain", "graphs-effects", "graphs-warmup" },
        ["picker"] = new[] { "no-results", "filtered" },
        ["settings"] = new[] { "theme-dropdown", "invalid-threshold", "invalid-limit", "invalid-hotkey", "restart-required", "startup-checking", "startup-error", "update-checking", "update-error",
            "category-appearance", "category-monitoring", "category-alerts", "category-overlay", "category-system" },
        ["fans"] = new[] { "off", "on", "manual", "curve", "pump", "modified", "no-channels", "conflict", "recovery", "write-failed" },
        ["peaks"] = new[] { "empty", "populated", "long-names" },
        ["alerts"] = new[] { "empty", "ongoing" },
        ["details"] = new[] { "gap", "long-unit", "thresholds", "detail-plain", "detail-smooth", "detail-warmup" },
        ["overlay"] = new[] { "move-mode", "vertical", "light-parent", "opacity-min", "opacity-max", "long-value" },
        ["threshold-dialog"] = new[] { "valid", "invalid", "lower-is-worse" },
        ["input-dialog"] = Array.Empty<string>(),
    };

    public static IReadOnlyList<string> Views { get; } = ByView.Keys.ToArray();

    /// <summary>Throws with a clear message when a requested substate makes no sense for the view, per
    /// PREVIEW_HARNESS.md ("Where a substate makes no sense for a view, print a clear error").</summary>
    public static void Validate(string view, IReadOnlyList<string> substates)
    {
        if (!ByView.TryGetValue(view, out var allowed))
            throw new ArgumentException($"Unknown --view '{view}'. Valid views: {string.Join(", ", Views)}");
        foreach (var s in substates)
        {
            if (!allowed.Contains(s))
                throw new ArgumentException(
                    $"--substate '{s}' does not apply to --view '{view}'. Valid substates for '{view}': {(allowed.Length == 0 ? "(none)" : string.Join(", ", allowed))}");
        }
    }
}
