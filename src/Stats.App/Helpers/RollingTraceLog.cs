using System.Diagnostics;
using System.IO;
using Stats.Core.Diagnostics;

namespace Stats.App.Helpers;

/// <summary>Rolling daily trace log under %AppData%\Stats\logs\stats-YYYYMMDD.log. <see cref="Install"/> is
/// called first in App.OnStartup, before any sensor/fan initialization, so those failures have a listener to
/// land in. Installation and pruning are both best-effort: a logging failure must never prevent startup.</summary>
internal static class RollingTraceLog
{
    private const int FilesToKeep = 7;

    /// <summary>The logs folder, once <see cref="Install"/> has successfully created it; null if logging
    /// could not be set up (e.g. Open log folder falls back to recomputing the path).</summary>
    public static string? LogDirectory { get; private set; }

    public static void Install()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats", "logs");
            Directory.CreateDirectory(dir);

            var path = Path.Combine(dir, $"stats-{DateTime.Now:yyyyMMdd}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream) { AutoFlush = true };
            Trace.Listeners.Add(new TextWriterTraceListener(writer, "StatsRollingLog"));
            Trace.AutoFlush = true;

            LogDirectory = dir; // only after the listener is live, so LogDirectory never points at an unwritten folder
            Prune(dir);
        }
        catch (Exception)
        {
            // Best-effort: a logging setup failure must not block sensor/fan startup.
        }
    }

    private static void Prune(string dir)
    {
        try
        {
            var names = Directory.EnumerateFiles(dir, "stats-*.log").Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).ToList();
            foreach (var name in LogRetention.SelectFilesToPrune(names, FilesToKeep))
            {
                try { File.Delete(Path.Combine(dir, name)); }
                catch (Exception ex) { Trace.WriteLine("[Stats] log prune failed for " + name + ": " + ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine("[Stats] log prune enumeration failed: " + ex.Message);
        }
    }
}
