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
    public string Message => $"{DisplayName} {PeakText} — crit {Symbol} {ThresholdText}";

    /// <summary>Toast title, e.g. "CPU Package critical".</summary>
    public string NotificationTitle => $"{DisplayName} critical";

    /// <summary>Toast body, e.g. "96 °C for 10 s (crit ≥ 92)" — peak via ValueFormatter exactly like Message and
    /// the Peaks row (MetricDefinition built with the default "F0" format, so "96.4" reads "96" everywhere); the
    /// threshold is a bare "0.#" invariant number; "≤" for lower-is-worse rules.</summary>
    public string NotificationBody(int holdSeconds) =>
        $"{PeakText} for {holdSeconds} s (crit {Symbol} {ThresholdText})";

    private string PeakText
    {
        get
        {
            var def = new MetricDefinition(MetricId, DisplayName, default, "", Unit);
            return ValueFormatter.Format(def, PeakValue);
        }
    }

    private string Symbol => LowerIsWorse ? "≤" : "≥";

    private string ThresholdText => Threshold.ToString("0.#", CultureInfo.InvariantCulture);
}
