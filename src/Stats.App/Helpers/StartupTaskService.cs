using System.Diagnostics;
using System.IO;
using Stats.Core.Startup;

namespace Stats.App.Helpers;

/// <summary>Result of one <c>schtasks.exe</c> invocation. <see cref="ExitCode"/> is the process exit code (0 =
/// success for /Create and /Delete; 0 = task exists for /Query). <see cref="StandardOutput"/>/
/// <see cref="StandardError"/> are captured for inline diagnostics — never shown to a shell, never re-parsed as
/// commands.</summary>
public sealed record StartupTaskResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}

/// <summary>Async wrapper around the "Stats" logon Scheduled Task used by the in-app Settings "Start Stats when I
/// sign in" checkbox. Argument construction is delegated to the pure, testable
/// <see cref="StartupTaskCommands"/> in Stats.Core; this class only owns spawning <c>schtasks.exe</c> —
/// <c>Process</c> stays out of Stats.Core. Every invocation uses <c>ArgumentList</c> (never a single
/// shell-parsed string) with <c>UseShellExecute = false</c>, so no argument — including the quoted executable
/// path — is ever re-interpreted by a shell.</summary>
public sealed class StartupTaskService
{
    private static readonly string SchtasksPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe");

    /// <summary>True if task "Stats" currently exists (exit code 0 from <c>/Query</c>).</summary>
    public async Task<StartupTaskResult> QueryAsync(CancellationToken ct = default) =>
        await RunAsync(StartupTaskCommands.Query(), ct).ConfigureAwait(false);

    /// <summary>Creates/replaces the logon task to launch <paramref name="executablePath"/> minimized, matching
    /// the installer's Scheduled Task exactly (see <see cref="StartupTaskCommands.Create"/>).</summary>
    public async Task<StartupTaskResult> CreateAsync(string executablePath, CancellationToken ct = default) =>
        await RunAsync(StartupTaskCommands.Create(executablePath), ct).ConfigureAwait(false);

    /// <summary>Removes the logon task.</summary>
    public async Task<StartupTaskResult> DeleteAsync(CancellationToken ct = default) =>
        await RunAsync(StartupTaskCommands.Delete(), ct).ConfigureAwait(false);

    private static async Task<StartupTaskResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(SchtasksPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in arguments) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();

        // Read both streams concurrently with WaitForExitAsync so a chatty schtasks.exe can't deadlock this
        // (the OS pipe buffer for either stream could otherwise fill while we're blocked reading the other).
        var stdOutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stdErrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stdOut = await stdOutTask.ConfigureAwait(false);
        var stdErr = await stdErrTask.ConfigureAwait(false);

        return new StartupTaskResult(process.ExitCode, stdOut.Trim(), stdErr.Trim());
    }
}
