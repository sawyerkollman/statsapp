using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Stats.Core.Metrics;

namespace Stats.App.Tray;

/// <summary>
/// Renders a 32×32 tray icon: rounded square tinted by severity with the CPU temp digits.
/// Keeps the last two HICONs alive (the shell may still reference the previous one) and destroys older ones.
/// </summary>
internal sealed class TrayIconRenderer : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly Queue<IntPtr> _live = new();
    private string _lastKey = "";

    /// <summary>Returns a new Icon when text/severity changed, otherwise null (caller keeps the current one).</summary>
    public Icon? Render(string text, Severity severity)
    {
        var key = text + "|" + severity;
        if (key == _lastKey) return null;
        _lastKey = key;

        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.Clear(Color.Transparent);

            var bg = severity switch
            {
                Severity.Crit => Color.FromArgb(0xE0, 0x5A, 0x4F),
                Severity.Warn => Color.FromArgb(0xE6, 0xA2, 0x3C),
                _ => Color.FromArgb(0x2B, 0x2B, 0x2F),
            };
            using (var path = RoundedRect(new Rectangle(0, 0, 31, 31), 7))
            using (var brush = new SolidBrush(bg))
                g.FillPath(brush, path);

            float fontPx = text.Length >= 3 ? 13f : 17f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, Brushes.White, new RectangleF(0, 1, 32, 31), sf);
        }

        IntPtr h = bmp.GetHicon();
        _live.Enqueue(h);
        while (_live.Count > 2) DestroyIcon(_live.Dequeue());
        return Icon.FromHandle(h);
    }

    private static GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public void Dispose()
    {
        while (_live.Count > 0) DestroyIcon(_live.Dequeue());
    }
}
