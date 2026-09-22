namespace Stats.Core.Startup;

/// <summary>Pure command-line parsing for launch flags. Deliberately has no Process/Environment access so it
/// stays in Stats.Core and is testable without coupling Core to Stats.App/WPF; the caller (App.OnStartup)
/// supplies the actual <c>StartupEventArgs.Args</c>.</summary>
public static class StartupArgs
{
    public const string MinimizedFlag = "--minimized";

    /// <summary>True if any argument is <see cref="MinimizedFlag"/>, compared case-insensitively (the installer's
    /// Scheduled Task and any manual shortcut may pass it in any casing).</summary>
    public static bool HasMinimizedFlag(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, MinimizedFlag, StringComparison.OrdinalIgnoreCase));

    public static int? InstallerOutcome(IEnumerable<string> args, string? installedVersion = null)
    {
        var values = args.ToArray();
        var index = Array.FindIndex(values, a => string.Equals(a, "--update-exit-code", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= values.Length || !int.TryParse(values[index + 1], out var code))
            return null;
        var expectedIndex = Array.FindIndex(values, a => a == "--update-expected");
        if (code == 0 && expectedIndex >= 0 && expectedIndex + 1 < values.Length &&
            !string.Equals(values[expectedIndex + 1].TrimStart('v'), installedVersion?.Split('+')[0], StringComparison.OrdinalIgnoreCase))
            return -1;
        return code;
    }

    public static string? InstallerFailure(IEnumerable<string> args) => InstallerFailure(InstallerOutcome(args));

    public static string? InstallerFailure(int? code)
    {
        if (code is null or 0) return null;
        if (code == 3010) return "Stats update requires a Windows restart. Save your work and restart Windows before retrying an update.";
        if (code == -1) return "The installer returned success but Stats is still running a different version. Restart Windows, then check for updates and retry.";
        return $"Installer did not finish (exit code {code}). Close other Stats copies, then check for updates and retry. Setup details: Windows\\Temp\\Stats-update-*\\setup.log.";
    }
}
