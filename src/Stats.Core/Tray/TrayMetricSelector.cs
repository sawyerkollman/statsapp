using Stats.Core.Metrics;
using Stats.Core.ViewModels;

namespace Stats.Core.Tray;

/// <summary>Resolves <see cref="Settings.AppSettings.TrayMetricId"/> to the definition the tray icon should render,
/// and lists the candidates offered by the Settings "Tray" picker. Core-only (no WPF): the composition root owns
/// the actual icon/tooltip rendering and the Auto fallback heuristic.</summary>
public static class TrayMetricSelector
{
    /// <summary>Null (Auto) or a missing id both resolve to null — the caller falls back to its own heuristic.</summary>
    public static MetricDefinition? Resolve(string? id, IReadOnlyList<MetricDefinition> definitions)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return definitions.FirstOrDefault(d => d.Id == id);
    }

    /// <summary>Every discovered °C or % metric, ordered the same way the dashboard groups tiles
    /// (<see cref="DashboardViewModel.GroupOrder"/>) then by display name.</summary>
    public static IReadOnlyList<MetricDefinition> Candidates(IReadOnlyList<MetricDefinition> definitions) =>
        definitions
            .Where(d => d.Unit is "°C" or "%")
            .OrderBy(d => Array.IndexOf(DashboardViewModel.GroupOrder, d.Group))
            .ThenBy(d => d.DisplayName, StringComparer.Ordinal)
            .ToList();
}
