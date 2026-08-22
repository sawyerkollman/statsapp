using System.Runtime.InteropServices;

namespace Stats.Core.Frames;

/// <summary>PID of the process owning the foreground window; null when there is none or it is this process.</summary>
public static class ForegroundProcess
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public static int? CurrentPid()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        if (GetWindowThreadProcessId(hwnd, out uint pid) == 0 || pid == 0) return null;
        if (pid == (uint)Environment.ProcessId) return null;
        return (int)pid;
    }
}
