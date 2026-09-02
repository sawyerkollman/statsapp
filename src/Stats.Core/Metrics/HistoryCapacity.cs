namespace Stats.Core.Metrics;

public static class HistoryCapacity
{
    public const int Min = 30;
    public const int Max = 3600;

    /// <summary>Samples needed to cover <paramref name="minutes"/> at <paramref name="pollSeconds"/>, clamped to [30, 3600].</summary>
    public static int Compute(int minutes, double pollSeconds)
    {
        if (pollSeconds <= 0) pollSeconds = 1;
        var samples = (int)Math.Round(minutes * 60.0 / pollSeconds);
        return Math.Clamp(samples, Min, Max);
    }

    /// <summary>Compact label for an effective history-window duration in seconds (e.g. buffer capacity × poll
    /// interval), so a requested window that was clamped to fit reports the duration it actually covers rather
    /// than the requested one. Whole minutes read "Xm", sub-minute windows read "Xs", and anything in between
    /// reads "XmYs".</summary>
    public static string FormatWindow(double totalSeconds)
    {
        var whole = Math.Max(0, (int)Math.Round(totalSeconds));
        if (whole < 60) return $"{whole}s";
        var minutes = whole / 60;
        var seconds = whole % 60;
        return seconds == 0 ? $"{minutes}m" : $"{minutes}m{seconds}s";
    }
}
