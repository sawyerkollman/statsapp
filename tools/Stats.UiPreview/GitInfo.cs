using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Stats.UiPreview;

/// <summary>Source identity for a capture's sidecar JSON: the commit HEAD is on, and a stable identity for any
/// uncommitted diff ("clean" when there is none). Best-effort — a machine without git on PATH (or run outside a
/// git checkout) still produces captures, just with "unknown" identity fields, never a crash.</summary>
internal static class GitInfo
{
    public static (string Commit, string DirtyDiffIdentity) Read()
    {
        var commit = Run("rev-parse", "HEAD") ?? "unknown";
        var diff = Run("diff");
        string identity;
        if (diff is null) identity = "unknown";
        else if (diff.Length == 0) identity = "clean";
        else identity = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(diff))).ToLowerInvariant();
        return (commit.Trim(), identity);
    }

    private static string? Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                WorkingDirectory = Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEndAsync();
            var errors = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(5000))
            {
                proc.Kill(entireProcessTree: true);
                return null;
            }
            if (!Task.WhenAll(output, errors).Wait(1000)) return null;
            return proc.ExitCode == 0 ? output.Result : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
