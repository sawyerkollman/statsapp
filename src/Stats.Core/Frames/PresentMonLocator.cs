namespace Stats.Core.Frames;

/// <summary>Finds the PresentMon executable: next to the app (installed), else installer/vendor (run from source).</summary>
public static class PresentMonLocator
{
    public const string ShippedFileName = "PresentMon.exe";

    public static string? Find(string? baseDirectory = null)
    {
        var dir = baseDirectory ?? AppContext.BaseDirectory;
        var shipped = Path.Combine(dir, ShippedFileName);
        if (File.Exists(shipped)) return shipped;

        // Walk up from bin/<cfg>/<tfm>/ to the repo root looking for installer/vendor/PresentMon-*.exe.
        var probe = new DirectoryInfo(dir);
        for (int i = 0; i < 8 && probe is not null; i++, probe = probe.Parent)
        {
            var vendor = Path.Combine(probe.FullName, "installer", "vendor");
            if (!Directory.Exists(vendor)) continue;
            var hit = Directory.EnumerateFiles(vendor, "PresentMon-*.exe").OrderByDescending(f => f).FirstOrDefault();
            if (hit is not null) return hit;
        }
        return null;
    }
}
