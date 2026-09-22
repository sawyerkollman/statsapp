using System.Text.Json;

namespace Stats.Core.Recording;

public sealed record SessionLibraryEntry(string Path, string GameName, DateTime StartedUtc, bool IsPinned, string? Error)
{
    public string DisplayName => Error is null ? $"{StartedUtc.ToLocalTime():g} · {System.IO.Path.GetFileName(Path)}" : $"{System.IO.Path.GetFileName(Path)} · {Error}";
    public bool CanOpen => Error is null;
}

public sealed class SessionLibrary
{
    private const int MaxFiles = 200, MaxIndexBytes = 128 * 1024;
    private readonly string _directory;
    private bool _indexReadable = true;
    private string? _indexError;
    public string? Error { get; private set; }
    public string? PinnedPath { get; private set; }

    public SessionLibrary(string directory) { _directory = directory; LoadIndex(); }

    public IReadOnlyList<SessionLibraryEntry> Read()
    {
        Error = _indexError;
        try
        {
            if (!Directory.Exists(_directory)) return [];
            return Directory.EnumerateFiles(_directory, "*.stats-session.jsonl")
                .Select(path => new FileInfo(path)).OrderByDescending(file => file.LastWriteTimeUtc).Take(MaxFiles)
                .Select(file => ReadEntry(file.FullName, file.LastWriteTimeUtc)).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Error = "Library unavailable: " + ex.Message; return []; }
    }

    public void SetPinned(string? path)
    {
        if (!_indexReadable) throw new InvalidDataException("Library index is malformed; fix or remove it before changing the pinned baseline.");
        if (path is not null) path = NormalizePinnedPath(_directory, path);
        var index = Path.Combine(_directory, ".library.json"); var temp = index + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(new LibraryIndex { PinnedPath = path }));
            if (new FileInfo(temp).Length > MaxIndexBytes) throw new InvalidDataException("Library index exceeds 128 KB.");
            File.Move(temp, index, true); PinnedPath = path; Error = null;
        }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private SessionLibraryEntry ReadEntry(string path, DateTime fallback)
    {
        try
        {
            using var reader = new StreamReader(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var header = ReadHeader(reader) ?? throw new InvalidDataException("Missing session header.");
            using var document = JsonDocument.Parse(header);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "header" || root.GetProperty("version").GetInt32() is not (1 or 2)) throw new InvalidDataException("Unsupported session format.");
            var started = root.GetProperty("startedUtc").GetDateTime().ToUniversalTime();
            var game = root.GetProperty("version").GetInt32() == 2 && root.TryGetProperty("gameName", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (game?.Length > 1024) throw new InvalidDataException("Session game name is too long.");
            return new(path, string.IsNullOrWhiteSpace(game) ? "Unassigned" : game!, started, string.Equals(path, PinnedPath, StringComparison.OrdinalIgnoreCase), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or KeyNotFoundException or FormatException or InvalidOperationException)
        { return new(path, "Unreadable", fallback, false, ex.Message); }
    }

    private void LoadIndex()
    {
        var path = Path.Combine(_directory, ".library.json");
        if (!File.Exists(path)) return;
        try
        {
            if (new FileInfo(path).Length > MaxIndexBytes) throw new InvalidDataException("Library index exceeds 128 KB.");
            PinnedPath = JsonSerializer.Deserialize<LibraryIndex>(File.ReadAllText(path))?.PinnedPath;
            if (PinnedPath is not null) PinnedPath = NormalizePinnedPath(_directory, PinnedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NotSupportedException)
        { PinnedPath = null; _indexReadable = false; Error = _indexError = "Library index unavailable: " + ex.Message; }
    }

    public static string NormalizePinnedPath(string directory, string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new InvalidDataException("Pinned baseline must be a local recording in the recording library.");
        var fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(fullPath), Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            !fullPath.EndsWith(".stats-session.jsonl", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Pinned baseline must be a recording inside this library.");
        return fullPath;
    }

    private static string? ReadHeader(TextReader reader)
    {
        const int limit = 2 * 1024 * 1024;
        var buffer = new System.Text.StringBuilder();
        for (var next = reader.Read(); next >= 0 && next != '\n'; next = reader.Read())
        { if (buffer.Length >= limit) throw new InvalidDataException("Session header exceeds 2 MB."); buffer.Append((char)next); }
        return buffer.Length == 0 ? null : buffer.ToString().TrimEnd('\r');
    }

    private sealed class LibraryIndex { public string? PinnedPath { get; set; } }
}
