using System.Globalization;

namespace Stats.Core.Processes;

public static class ProcessFormat
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * MiB;
    public static string Percent(float? value) => value is { } v ? v.ToString("F1", CultureInfo.InvariantCulture) + " %" : "\u2014";
    public static string Bytes(long bytes) => bytes >= GiB
        ? (bytes / (double)GiB).ToString("F1", CultureInfo.InvariantCulture) + " GB"
        : (bytes / (double)MiB).ToString("F0", CultureInfo.InvariantCulture) + " MB";
    public static string Megabytes(long bytes) => (bytes / (double)MiB).ToString("0.#", CultureInfo.InvariantCulture);
    public static string PercentTsv(float? value) => value is { } v ? v.ToString("0.#", CultureInfo.InvariantCulture) : "";
}
