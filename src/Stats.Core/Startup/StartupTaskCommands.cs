using Stats.Core.Platform;

namespace Stats.Core.Startup;

/// <summary>Pure <c>schtasks.exe</c> argument construction for the "Stats" logon task, shared in shape with the
/// installer's Scheduled Task (<c>installer/Stats.iss</c> [Run] section) so the two can be compared during
/// review. Deliberately builds only string arguments — no <c>Process</c> — so this stays in Stats.Core and is
/// testable without a Windows process; the caller (Stats.App's StartupTaskService) does the actual invocation.
/// </summary>
public static class StartupTaskCommands
{
    /// <summary>Scheduled Task name used by both the installer and the in-app Settings toggle.</summary>
    public const string TaskName = "Stats";

    /// <summary>Query whether the task exists. <c>schtasks /Query</c> exits 0 if found, non-zero otherwise.</summary>
    public static IReadOnlyList<string> Query() => new[] { "/Query", "/TN", TaskName };

    /// <summary>Remove the task; <c>/F</c> suppresses the "are you sure" prompt so this is safe unattended even
    /// if the task does not exist (schtasks still exits non-zero in that case, which callers treat as "already
    /// gone", not a failure to surface).</summary>
    public static IReadOnlyList<string> Delete() => new[] { "/Delete", "/F", "/TN", TaskName };

    /// <summary>True when <paramref name="executablePath"/> is a file strictly inside an admin-only install
    /// directory (Program Files, Program Files (x86), or <paramref name="adminOnlyRoots"/>). A highest-privilege
    /// logon task must not point at a user-writable copy of the exe: anyone who can replace that file would
    /// run as admin at the next sign-in.</summary>
    public static bool IsSafeHighestPrivilegeTarget(string? executablePath, IEnumerable<string>? adminOnlyRoots = null) =>
        ProgramFilesPath.IsUnder(executablePath, adminOnlyRoots);

    /// <summary>Builds the same logon Scheduled Task the installer creates: <c>/SC ONLOGON /RL HIGHEST /IT</c>
    /// (highest privilege, interactive token — starts the requireAdministrator app at logon without a UAC
    /// prompt), running <paramref name="executablePath"/> quoted plus <see cref="StartupArgs.MinimizedFlag"/>.
    /// The exe path is quoted because Task Scheduler splits the stored /TR command into program + arguments
    /// itself at run time, and an unquoted "Program Files" path would be split on its own space.
    /// Refuses a path that is not under Program Files — see <see cref="IsSafeHighestPrivilegeTarget"/>.</summary>
    public static IReadOnlyList<string> Create(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path is required.", nameof(executablePath));
        if (!IsSafeHighestPrivilegeTarget(executablePath))
            throw new ArgumentException(
                "Start when I sign in can only launch Stats from Program Files. A copy in a user-writable folder cannot be registered as an administrator logon task.",
                nameof(executablePath));

        return new[]
        {
            "/Create", "/F", "/TN", TaskName,
            "/TR", $"\"{executablePath}\" {StartupArgs.MinimizedFlag}",
            "/SC", "ONLOGON",
            "/RL", "HIGHEST",
            "/IT",
        };
    }
}
