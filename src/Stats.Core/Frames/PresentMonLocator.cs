using Stats.Core.Platform;

namespace Stats.Core.Frames;

/// <summary>Finds the PresentMon executable: next to the app (installed), else — debug runs that are not under
/// Program Files — <c>installer/vendor</c> when launching from a source tree. Release builds and anything under
/// an installed layout never walk parent directories. That walk is what turns a missing bundled binary into a
/// privilege escalation: from <c>C:\Program Files\Stats</c> the parents include <c>C:\</c>, and a standard user
/// can plant <c>C:\installer\vendor\PresentMon-*.exe</c> for this admin process to launch.</summary>
public static class PresentMonLocator
{
    public const string ShippedFileName = "PresentMon.exe";

#if DEBUG
    public static bool IsDebugBuild { get; } = true;
#else
    public static bool IsDebugBuild { get; } = false;
#endif

    /// <summary>The parent-folder vendor probe. False for release builds and for any base directory that already
    /// sits under Program Files (or another root in <paramref name="installedRoots"/>).</summary>
    public static bool AllowsVendorWalk(string? baseDirectory, bool debugBuild, IEnumerable<string>? installedRoots = null)
    {
        if (!debugBuild || string.IsNullOrWhiteSpace(baseDirectory)) return false;
        return !ProgramFilesPath.IsUnder(baseDirectory, installedRoots);
    }

    public static string? Find(string? baseDirectory = null, bool? allowVendorWalk = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var shipped = Path.Combine(dir, ShippedFileName);
        if (File.Exists(shipped)) return shipped;

        // A caller can force the walk on (tests, a debug source run) but cannot force it for an installed
        // layout: Program Files stays closed even when allowVendorWalk is passed true.
        var walk = allowVendorWalk ?? AllowsVendorWalk(dir, IsDebugBuild);
        if (walk && !AllowsVendorWalk(dir, debugBuild: true)) walk = false;
        if (!walk) return null;

        // Walk up from bin/<cfg>/<tfm>/ toward the repo root looking for installer/vendor/PresentMon-*.exe.
        // Stop at a drive root so C:\installer\vendor is never a candidate.
        var probe = new DirectoryInfo(dir);
        for (int i = 0; i < 8 && probe is not null; i++, probe = probe.Parent)
        {
            if (ProgramFilesPath.IsDriveRoot(probe.FullName)) break;
            var vendor = Path.Combine(probe.FullName, "installer", "vendor");
            if (!Directory.Exists(vendor)) continue;
            var hit = Directory.EnumerateFiles(vendor, "PresentMon-*.exe").OrderByDescending(f => f).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }
}
