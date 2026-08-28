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
}
