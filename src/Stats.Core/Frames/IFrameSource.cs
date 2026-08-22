namespace Stats.Core.Frames;

/// <summary>A running producer of PresentMon CSV lines (normally the PresentMon child process).</summary>
public interface IFrameSource : IDisposable
{
    /// <summary>Raw stdout line. Raised on the source's reader thread.</summary>
    event Action<string>? LineReceived;
    /// <summary>The source ended on its own: (exit code, last lines of stderr). Not raised by Stop().</summary>
    event Action<int, string>? Exited;
    bool IsRunning { get; }
    /// <exception cref="System.ComponentModel.Win32Exception">The executable could not be started.</exception>
    void Start();
    void Stop();
}
