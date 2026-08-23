using System.Diagnostics;
namespace Stats.Core.Fans;
/// <summary>Known fan-control tools that would fight Stats for the same PWM outputs.</summary>
public static class ConflictingFanSoftware
{
    private static readonly (string Stem, string Friendly, bool Exact)[] Known =
    {
        ("msi center", "MSI Center", false), ("msi.centralserver", "MSI Center", false), ("msi center sdk", "MSI Center", false),
        ("fancontrol", "Fan Control", false), ("argusmonitor", "Argus Monitor", false), ("msiafterburner", "MSI Afterburner", false),
        ("icue", "Corsair iCUE", false), ("nzxt cam", "NZXT CAM", false),
        ("armourycrate", "ASUS Armoury Crate", false), ("armoury crate", "ASUS Armoury Crate", false), ("asusfanservice", "ASUS Armoury Crate", false),
        ("speedfan", "SpeedFan", false), ("gigabytecontrolcenter", "Gigabyte Control Center", false), ("aorusengine", "Gigabyte Control Center", false), ("gcc", "Gigabyte Control Center", true),
        ("precisionx", "EVGA Precision", false), ("corsairlink", "Corsair Link", false),
    };

    public static IReadOnlyList<string> Match(IEnumerable<string> processNames)
    {
        var result = new List<string>();
        foreach (var raw in processNames)
        {
            var name = raw.Trim();
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            foreach (var (stem, friendly, exact) in Known)
            {
                bool hit = exact ? name.Equals(stem, StringComparison.OrdinalIgnoreCase)
                                 : name.Contains(stem, StringComparison.OrdinalIgnoreCase);
                if (hit && !result.Contains(friendly)) { result.Add(friendly); break; }
            }
        }
        return result;
    }

    public static IEnumerable<string> RunningProcessNames()
    {
        try { return Process.GetProcesses().Select(p => { try { return p.ProcessName; } catch { return ""; } }).ToList(); }
        catch { return Array.Empty<string>(); }
    }
}
