namespace Stats.UiPreview.Fixtures;

/// <summary>Records every simulated side effect a preview composition performs — fan writes, save-settings,
/// "open log folder", startup toggle, restart, manual update check, etc. Nothing here ever reaches a real
/// service; this is the fixture log PREVIEW_HARNESS.md requires ("all commands record intended effects to a
/// fixture log") and what the isolation/fan-command-recording tests assert against. One instance is shared by an
/// entire batch run's temp root and flushed to commands.log at the end.</summary>
public sealed class CommandLog
{
    private readonly List<string> _entries = new();
    private readonly object _gate = new();

    public IReadOnlyList<string> Entries
    {
        get { lock (_gate) return _entries.ToList(); }
    }

    public void Record(string message)
    {
        lock (_gate) _entries.Add($"{DateTime.UtcNow:O} {message}");
    }

    public void WriteTo(string path)
    {
        List<string> snapshot;
        lock (_gate) snapshot = _entries.ToList();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllLines(path, snapshot);
    }
}
