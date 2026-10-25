using System.Text;

namespace Stats.Core.Updates;

/// <summary>The hidden cmd.exe helper that waits for this process to exit and then relaunches Stats (and, for an
/// update, runs the installer first). Written as UTF-8 with a BOM and <c>chcp 65001</c> so a non-ASCII install
/// path is not mangled, and with <c>%</c> doubled so a path containing a percent sign is not expanded as a
/// variable. Delayed expansion is off so <c>!</c> in a path stays literal.</summary>
public static class HelperCmdScript
{
    /// <summary>UTF-8 with BOM. cmd.exe switches to UTF-8 when it sees the BOM; <c>chcp 65001</c> in the script
    /// covers hosts that ignore the BOM. <see cref="Encoding.GetBytes(string)"/> does not emit the preamble, so
    /// callers write <see cref="GetBytes"/>.</summary>
    public static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static byte[] GetBytes(string script)
    {
        var body = Utf8.GetBytes(script);
        var bom = Utf8.GetPreamble();
        var bytes = new byte[bom.Length + body.Length];
        Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
        Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
        return bytes;
    }

    public static string BuildUpdateScript(int pid, string installerPath, string exePath) =>
        WaitLoop(pid) +
        $"{Quote(installerPath)} /SILENT /NOCANCEL\r\n" +
        $"start \"\" {Quote(exePath)}\r\n";

    public static string BuildRestartScript(int pid, string exePath) =>
        WaitLoop(pid) +
        $"start \"\" {Quote(exePath)}\r\n";

    /// <summary>Waits up to 2 minutes (120 * ~1 s ping) for this PID to exit before giving up — bounds the loop
    /// so a wedged app can't leave cmd.exe polling forever. <c>ping -n 2 127.0.0.1</c> is used instead of
    /// <c>timeout /t 1</c> because timeout fails outright when its stdin is redirected (the helper is launched
    /// hidden with UseShellExecute false).</summary>
    private static string WaitLoop(int pid) =>
        "@echo off\r\n" +
        "chcp 65001 >nul\r\n" +
        "setlocal DisableDelayedExpansion\r\n" +
        "set n=0\r\n" +
        ":wait\r\n" +
        $"tasklist /FI \"PID eq {pid}\" | find \"{pid}\" >nul\r\n" +
        "if errorlevel 1 goto run\r\n" +
        "set /a n+=1\r\n" +
        "if %n% gtr 120 exit /b\r\n" +
        "ping -n 2 127.0.0.1 >nul\r\n" +
        "goto wait\r\n" +
        ":run\r\n";

    /// <summary>Quotes a path for cmd and doubles <c>%</c>. Quotes and newlines cannot be escaped inside a
    /// quoted cmd token, so they are rejected instead of written into the script.</summary>
    private static string Quote(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));
        if (path.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0)
            throw new ArgumentException("Path cannot contain quotes or line breaks.", nameof(path));
        return "\"" + path.Replace("%", "%%") + "\"";
    }
}
