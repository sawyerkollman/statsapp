using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Stats.App.Helpers;

/// <summary>Toggles WS_EX_TRANSPARENT so the window ignores mouse input (clicks fall through to what's beneath).</summary>
internal static class ClickThrough
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    public static void Set(Window window, bool enabled)
    {
        try
        {
            var h = new WindowInteropHelper(window).EnsureHandle();
            int style = GetWindowLong(h, GWL_EXSTYLE);
            style = enabled ? style | WS_EX_TRANSPARENT | WS_EX_LAYERED : style & ~WS_EX_TRANSPARENT;
            SetWindowLong(h, GWL_EXSTYLE, style);
        }
        catch { /* best effort */ }
    }
}
