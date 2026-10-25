namespace Stats.Core.Startup;

/// <summary>Pure command-line parsing for launch flags. Deliberately has no Process/Environment access so it
/// stays in Stats.Core and is testable without coupling Core to Stats.App/WPF; the caller (App.OnStartup)
/// supplies the actual <c>StartupEventArgs.Args</c>.</summary>
public static class StartupArgs
{
    public const string MinimizedFlag = "--minimized";

    /// <summary>Installer-only. Open hardware, hand every software-controlled fan back to the device, and exit
    /// without showing a window. Used after a forced close so uninstall and install-over-running do not leave
    /// PWM stuck. Keep the spelling in sync with <c>installer/Stats.iss</c>.</summary>
    public const string RestoreFansFlag = "--restore-fans";

    /// <summary>Auto-reset named event. The installer signals it so a running Stats can take its normal exit
    /// path (poller stop, <c>FanController.RestoreAll</c>, settings saved) instead of being killed. Admins and
    /// SYSTEM only. Keep the spelling in sync with <c>ExitEventName</c> in <c>installer/Stats.iss</c>.</summary>
    public const string ExitEventName = @"Local\Stats.RequestExit";

    /// <summary>True if any argument is <see cref="MinimizedFlag"/>, compared case-insensitively (the installer's
    /// Scheduled Task and any manual shortcut may pass it in any casing).</summary>
    public static bool HasMinimizedFlag(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, MinimizedFlag, StringComparison.OrdinalIgnoreCase));

    /// <summary>True if any argument is <see cref="RestoreFansFlag"/>, compared case-insensitively.</summary>
    public static bool HasRestoreFansFlag(IEnumerable<string> args) =>
        args.Any(a => string.Equals(a, RestoreFansFlag, StringComparison.OrdinalIgnoreCase));
}
