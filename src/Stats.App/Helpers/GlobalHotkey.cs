using System.Runtime.InteropServices;
using System.Windows.Interop;
using Stats.Core.Settings;

namespace Stats.App.Helpers;

/// <summary>System-wide hotkey via RegisterHotKey on a message-only window. Raises Pressed on the UI thread.</summary>
internal sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const int HotkeyId = 0x5A17;
    private const uint MOD_NOREPEAT = 0x4000;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly HwndSource _source;
    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey()
    {
        var p = new HwndSourceParameters("StatsHotkeySink")
        {
            Width = 0, Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ParentWindow = HWND_MESSAGE,
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>Registers (replacing any previous). Null hotkey = unregister only. Returns false if the combo is taken.</summary>
    public bool Register(ParsedHotkey? hotkey)
    {
        Unregister();
        if (hotkey is null) return true;
        _registered = RegisterHotKey(_source.Handle, HotkeyId, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
