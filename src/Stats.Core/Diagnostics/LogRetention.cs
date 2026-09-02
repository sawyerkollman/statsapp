namespace Stats.Core.Diagnostics;

/// <summary>Pure file-name retention policy for the rolling trace log (stats-YYYYMMDD.log, one per day).
/// Deliberately has no Directory/File access so it stays in Stats.Core and is testable without coupling Core
/// to Stats.App/WPF; the caller (Stats.App's RollingTraceLog) does the actual enumeration and deletion.</summary>
public static class LogRetention
{
    /// <summary>Given the log file names currently on disk, returns the ones to delete so only the newest
    /// <paramref name="keep"/> remain. Ordinal sort on the file name is sufficient because stats-YYYYMMDD.log
    /// names already sort chronologically — no date parsing needed.</summary>
    public static IReadOnlyList<string> SelectFilesToPrune(IEnumerable<string> fileNames, int keep)
    {
        if (keep < 0) keep = 0;
        var sorted = fileNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
        int excess = sorted.Count - keep;
        return excess <= 0 ? Array.Empty<string>() : sorted.Take(excess).ToList();
    }
}
