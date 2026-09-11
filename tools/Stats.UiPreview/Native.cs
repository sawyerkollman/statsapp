using System.Runtime.InteropServices;

namespace Stats.UiPreview;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left, Top, Right, Bottom;
    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;
}

/// <summary>DWM window-frame P/Invoke used only for --method screen (System.Drawing.Graphics.CopyFromScreen of
/// the window's actual on-screen extended frame bounds — includes shadow-excluded, DPI-correct client+chrome
/// bounds, unlike Window.ActualWidth/Height which are logical units).</summary>
internal static class Native
{
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    /// <summary>A ContextMenu's default Placement is MousePoint — it opens at the real OS cursor position, not
    /// relative to PlacementTarget's bounds. A synthetic MouseRightButtonUp fires the handler regardless of where
    /// the cursor actually is, but the menu it opens would appear wherever the cursor happens to be parked in this
    /// environment (potentially off-window, invisible to a --method screen capture of just the window's rect).
    /// Moving the real cursor first makes the resulting popup appear where a real right-click would.</summary>
    public static void MoveCursorTo(int screenX, int screenY)
    {
        try { SetCursorPos(screenX, screenY); } catch (Exception) { /* best effort */ }
    }

    /// <summary>Blocks until the DWM compositor has actually presented its current frame. WPF's own dispatcher
    /// settling (ContentRendered/CompositionTarget.Rendering) only proves the Visual tree rendered into WPF's
    /// off-screen surface — DWM composites that onto the actual screen on its own thread/timer, slightly later.
    /// Without this, --method screen can capture a frame or two before the desktop compositor has caught up
    /// (observed as a blank/white capture even though --method rtb of the same window is correct).</summary>
    public static void FlushCompositor()
    {
        try { DwmFlush(); DwmFlush(); } catch (Exception) { /* best effort — composited desktop unavailable */ }
    }

    public static RECT GetExtendedFrameBounds(IntPtr hwnd)
    {
        int hr = DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>());
        if (hr != 0) throw new InvalidOperationException($"DwmGetWindowAttribute failed (hr=0x{hr:X8}).");
        return rect;
    }
}
