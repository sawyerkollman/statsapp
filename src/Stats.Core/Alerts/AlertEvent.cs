using System.Globalization;
using Stats.Core.Metrics;

namespace Stats.Core.Alerts;

/// <summary>A single alert raised by <see cref="AlertEngine"/>: the metric held Crit for at least HoldSeconds.
/// PeakValue is the worst value seen during the whole episode up to the moment of raising (tracking continues
/// afterwards but the event itself is a one-shot snapshot).</summary>
public sealed record AlertEvent(
    DateTime RaisedAtLocal,
    string MetricId,
    string DisplayName,
    string Unit,
    float PeakValue,
    float Threshold,
    bool LowerIsWorse)
{
    /// <summary>e.g. "CPU Package 96 °C — crit ≥ 92" (inverted rules read "crit ≤ 30"). Built with
    /// <see cref="ValueFormatter"/> so the peak reads exactly like the tile/tray does; the threshold is a bare
    /// number (unit already shown on the peak).</summary>
    public string Message
    {
        get
        {
            var def = new MetricDefinition(MetricId, DisplayName, default, "", Unit);
            var peakText = ValueFormatter.Format(def, PeakValue);
            var symbol = LowerIsWorse ? "≤" : "≥";
            var thresholdText = Threshold.ToString("0.#", CultureInfo.InvariantCulture);
            return $"{DisplayName} {peakText} — crit {symbol} {thresholdText}";
        }
    }
}
