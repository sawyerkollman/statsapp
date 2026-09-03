using System.Globalization;
using Stats.Core.Settings;

namespace Stats.Core.Metrics;

/// <summary>Pure parsing/validation for one Warn/Crit pair, shared by <c>Stats.App.Views.ThresholdDialog</c> and
/// <see cref="ViewModels.SettingsViewModel"/>'s threshold grid. Never silently drops bad input — every failure
/// returns a specific, user-facing message so a typo can't quietly discard someone's thresholds.</summary>
public static class ThresholdInput
{
    /// <summary>Parses <paramref name="warnText"/>/<paramref name="critText"/> and validates their ordering against
    /// <paramref name="lowerIsWorse"/> ("warn below crit" normally, "warn above crit" when lower is worse). Numbers
    /// parse with the invariant culture first (the format every other numeric field in Stats uses), falling back to
    /// the current culture so a comma-decimal locale still works when pasted straight from the OS.</summary>
    public static bool TryParse(string warnText, string critText, bool lowerIsWorse, out ThresholdRule rule, out string error)
    {
        rule = new ThresholdRule();
        error = "";

        if (!TryParseFloat(warnText, out var warn)) { error = "Warn must be a number"; return false; }
        if (!TryParseFloat(critText, out var crit)) { error = "Crit must be a number"; return false; }

        bool ordered = lowerIsWorse ? warn > crit : warn < crit;
        if (!ordered)
        {
            error = lowerIsWorse ? "Warn must be above crit when lower is worse" : "Warn must be below crit";
            return false;
        }

        rule = new ThresholdRule { Warn = warn, Crit = crit, LowerIsWorse = lowerIsWorse };
        return true;
    }

    private static bool TryParseFloat(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
        float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
}
