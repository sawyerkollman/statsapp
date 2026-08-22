using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Stats.App.Helpers;

/// <summary>Asks DWM for the dark (immersive) title bar. No-op on failure/old Windows.</summary>
internal static class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static void Apply(Window window)
    {
        void Set()
        {
            try
            {
                var h = new WindowInteropHelper(window).Handle;
                if (h == IntPtr.Zero) return;
                int on = 1;
                DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
            }
            catch { /* unsupported OS — keep default chrome */ }
        }
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Set();
        else window.SourceInitialized += (_, _) => Set();
    }
}
