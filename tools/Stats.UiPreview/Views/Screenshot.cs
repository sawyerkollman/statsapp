using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Stats.UiPreview.Views;

/// <summary>The two capture methods PREVIEW_HARNESS.md requires: "screen" (a real visible-window capture via
/// System.Drawing.Graphics.CopyFromScreen of the window's DWM extended-frame-bounds rect — includes popups,
/// dropdowns, and context menus that are on screen) and "rtb" (RenderTargetBitmap of the window's own root visual
/// — DPI-aware, but excludes native chrome/title bar and any separate popup HWND).</summary>
internal static class Screenshot
{
    /// <returns>(physicalWidth, physicalHeight, dpiX, dpiY)</returns>
    public static (int Width, int Height, double DpiX, double DpiY) CaptureScreen(Window window, string outputPath)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("Window has no HWND — was it shown?");
        var rect = Native.GetExtendedFrameBounds(hwnd);
        if (rect.Width <= 0 || rect.Height <= 0)
            throw new InvalidOperationException($"Window frame bounds are empty ({rect.Width}x{rect.Height}) — is it visible on the primary monitor?");

        // See Native.FlushCompositor: without this the desktop compositor can lag WPF's own render pass by a
        // frame or two, producing a blank/white capture even though the window's Visual tree is fully painted.
        Native.FlushCompositor();
        System.Threading.Thread.Sleep(120);
        Native.FlushCompositor();

        using var bmp = new System.Drawing.Bitmap(rect.Width, rect.Height);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
            g.CopyFromScreen(rect.Left, rect.Top, 0, 0, bmp.Size);
        bmp.Save(outputPath, System.Drawing.Imaging.ImageFormat.Png);
        return (rect.Width, rect.Height, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
    }

    public static (int Width, int Height, double DpiX, double DpiY) CaptureRenderTargetBitmap(Window window, string outputPath)
    {
        var dpi = VisualTreeHelper.GetDpi(window);
        int width = Math.Max(1, (int)Math.Round(window.ActualWidth * dpi.DpiScaleX));
        int height = Math.Max(1, (int)Math.Round(window.ActualHeight * dpi.DpiScaleY));
        var rtb = new RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        rtb.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using (var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
            encoder.Save(stream);
        return (width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY);
    }
}
