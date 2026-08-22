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
}
