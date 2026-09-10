using System.Diagnostics;
using System.Text;
using System.Windows.Diagnostics;

namespace Stats.UiPreview;

/// <summary>Captures WPF data-binding trace output (PresentationTraceSources.DataBindingSource) into a list so a
/// capture's sidecar JSON can record binding/resource-resolution warnings instead of them only reaching the debug
/// console. Attach with <see cref="Install"/> once per process; per-capture warnings are read via
/// <see cref="TakeAndClear"/>.</summary>
public sealed class BindingErrorListener : TraceListener
{
    private readonly List<string> _warnings = new();
    private readonly StringBuilder _pending = new();
    private readonly object _gate = new();

    public static BindingErrorListener Install()
    {
        var listener = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        PresentationTraceSources.ResourceDictionarySource.Listeners.Add(listener);
        PresentationTraceSources.ResourceDictionarySource.Switch.Level = SourceLevels.Error;
        return listener;
    }

    public override void Write(string? message)
    {
        if (message is null) return;
        lock (_gate) _pending.Append(message);
    }

    public override void WriteLine(string? message)
    {
        lock (_gate)
        {
            if (message is not null) _pending.Append(message);
            var text = _pending.ToString().Trim();
            _pending.Clear();
            if (text.Length > 0) _warnings.Add(text);
        }
    }

    /// <summary>Returns everything captured since the last call and clears the buffer — one capture's sidecar
    /// must not see warnings that actually belong to a previous capture in the same batch process.</summary>
    public List<string> TakeAndClear()
    {
        lock (_gate)
        {
            var copy = new List<string>(_warnings);
            _warnings.Clear();
            return copy;
        }
    }
}
