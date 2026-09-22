using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace Stats.App.Helpers;

/// <summary>Machine-wide ownership before any hardware starts. The session-local hidden window is also the
/// installer's v1 graceful-shutdown endpoint. No window is shown or activated here.</summary>
internal sealed class SingleInstance : IDisposable
{
    internal const string WindowName = "Stats.Control.v1";
    internal const string MutexName = @"Global\Stats.Native.v1";
    internal const int ShutdownMessage = 0x8001;
    private const int ShowMessage = 0x8002;
    private const int OutcomeMessage = 0x8003;
    private readonly Mutex _mutex;
    private readonly bool _owns;
    private HwndSource? _source;

    public SingleInstance()
    {
        var security = CreateSecurity();
        _mutex = MutexAcl.Create(false, MutexName, out _, security);
        try
        {
            // Never adopt or rewrite an object pre-created with a weaker owner/ACL.
            const AccessControlSections sections = AccessControlSections.Owner | AccessControlSections.Access;
            if (_mutex.GetAccessControl().GetSecurityDescriptorSddlForm(sections) != security.GetSecurityDescriptorSddlForm(sections))
                throw new UnauthorizedAccessException("The Stats ownership guard has an unexpected security descriptor.");
            try { _owns = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _owns = true; }
        }
        catch { _mutex.Dispose(); throw; }
    }

    private static MutexSecurity CreateSecurity()
    {
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var security = new MutexSecurity();
        security.SetOwner(administrators);
        security.SetAccessRuleProtection(true, false);
        security.AddAccessRule(new MutexAccessRule(administrators, MutexRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), MutexRights.FullControl, AccessControlType.Allow));
        return security;
    }

    public bool IsOwner => _owns;

    public void Listen(Action shutdown, Action show, Action<int> outcome)
    {
        if (!_owns) throw new InvalidOperationException("Only the primary instance may listen.");
        _source = new HwndSource(new HwndSourceParameters(WindowName)
        {
            Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000),
        });
        _source.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (message is ShutdownMessage or ShowMessage)
            {
                handled = true;
                // Never tear down a HWND from inside its native message callback.
                Dispatcher.CurrentDispatcher.BeginInvoke(message == ShutdownMessage ? shutdown : show);
            }
            if (message == OutcomeMessage)
            {
                handled = true;
                var code = wParam.ToInt32();
                Dispatcher.CurrentDispatcher.BeginInvoke(() => outcome(code));
            }
            return IntPtr.Zero;
        });
    }

    public static bool NotifyPrimary(bool show, int? outcome)
    {
        IntPtr window = IntPtr.Zero;
        for (var attempt = 0; attempt < 20 && window == IntPtr.Zero; attempt++)
        {
            window = FindWindow(null, WindowName);
            if (window == IntPtr.Zero) Thread.Sleep(50); // primary may still be creating its endpoint
        }
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out var pid);
        using var process = OpenProcess(0x1000, false, pid);
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        if (process.IsInvalid || !QueryFullProcessImageName(process, 0, path, ref length)
            || !IsCurrentExecutable(path.ToString(), Environment.ProcessPath)) return false;
        if (outcome is int code && !Deliver(window, OutcomeMessage, new IntPtr(code))) return false;
        if (!show) return true;
        AllowSetForegroundWindow(pid);
        return Deliver(window, ShowMessage, IntPtr.Zero);
    }

    private static bool IsCurrentExecutable(string candidate, string? current) =>
        !string.IsNullOrEmpty(current) && string.Equals(candidate, current, StringComparison.OrdinalIgnoreCase);

    private static bool Deliver(IntPtr window, int message, IntPtr value)
    {
        if (SendMessageTimeout(window, message, value, IntPtr.Zero, 2, 1000, out _) != IntPtr.Zero) return true;
        // Hardware discovery can occupy the primary UI thread during startup. Payload is one integer,
        // so a queued message safely survives this secondary process exiting.
        if (PostMessage(window, message, value, IntPtr.Zero)) return true;
        System.Diagnostics.Trace.WriteLine("[Stats] Could not notify the primary instance.");
        return false;
    }

    public void Dispose()
    {
        _source?.Dispose();
        if (_owns) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int size);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);
}
