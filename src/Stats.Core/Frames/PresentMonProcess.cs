using System.Diagnostics;

namespace Stats.Core.Frames;

/// <summary>
/// Owns one PresentMon console process capturing all presenters to stdout. Filtering to the process of
/// interest happens downstream so alt-tabbing never restarts the ETW session.
/// </summary>
public sealed class PresentMonProcess : IFrameSource
{
    private const int StderrTailLines = 20;
    private readonly string _exePath;
    private readonly string _excludeExeName;
    private readonly object _gate = new();
    private Process? _process;
    private readonly Queue<string> _stderr = new();

    public PresentMonProcess(string exePath, string excludeExeName = "Stats.App.exe")
    {
        _exePath = exePath;
        _excludeExeName = excludeExeName;
    }

    public event Action<string>? LineReceived;
    public event Action<int, string>? Exited;

    public bool IsRunning { get { lock (_gate) return _process is { HasExited: false }; } }

    public static string BuildArguments(string excludeExeName) =>
        $"--output_stdout --no_console_stats --stop_existing_session --session_name StatsFps " +
        $"--no_track_gpu --no_track_input --exclude \"{excludeExeName}\"";

    public void Start()
    {
        lock (_gate)
        {
            if (_process is { HasExited: false }) return;
            lock (_stderr) _stderr.Clear();
            var psi = new ProcessStartInfo(_exePath, BuildArguments(_excludeExeName))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                WorkingDirectory = Path.GetDirectoryName(_exePath) ?? Environment.CurrentDirectory,
            };
            var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data is not null) LineReceived?.Invoke(e.Data); };
            p.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_stderr) { _stderr.Enqueue(e.Data); while (_stderr.Count > StderrTailLines) _stderr.Dequeue(); }
            };
            p.Exited += OnExited;
            p.Start(); // throws Win32Exception if the exe cannot start
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            _process = p;
        }
    }

    private void OnExited(object? sender, EventArgs e)
    {
        if (sender is not Process p) return;
        bool isCurrent;
        lock (_gate)
        {
            isCurrent = ReferenceEquals(p, _process);
            if (isCurrent) _process = null;
        }
        if (!isCurrent)
        {
            // Superseded by Stop() or a later Start(); that call owns/owned disposal, but
            // dispose here too in case it raced us and never got to — Process.Dispose() is idempotent.
            try { p.Dispose(); } catch { /* already gone */ }
            return;
        }
        try { p.WaitForExit(); } catch { /* flushes the async stdout/stderr readers so the stderr tail is complete */ }
        int code = SafeExitCode(p);
        p.Dispose();
        string tail;
        lock (_stderr) tail = string.Join(Environment.NewLine, _stderr);
        Exited?.Invoke(code, tail);
    }

    public void Stop()
    {
        Process? p;
        lock (_gate)
        {
            p = _process;
            _process = null;
        }
        if (p is null) return;
        try
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            p.WaitForExit(2000);
        }
        catch { /* already gone */ }
        finally { p.Dispose(); }
    }

    public void Dispose() => Stop();

    private static int SafeExitCode(Process p)
    {
        try { return p.ExitCode; } catch { return -1; }
    }
}
