using System.Text.Json;

namespace Stats.Core.Recording;

public sealed record SessionBookmark(long SampleIndex, string Note);
public sealed record SessionAnnotations(IReadOnlyList<SessionBookmark> Bookmarks)
{
    public const int MaxBookmarks = 100;
    public const int MaxNoteLength = 1000;
}

public static class SessionAnnotationStore
{
    public static string PathFor(string sessionPath) => sessionPath + ".annotations.json";
    public static SessionAnnotations Load(string sessionPath)
    {
        var path = PathFor(sessionPath);
        if (!File.Exists(path)) return new([]);
        try
        {
            if (new FileInfo(path).Length > 512 * 1024) throw new InvalidDataException("Session annotations exceed 512 KB.");
            var data = JsonSerializer.Deserialize<SessionAnnotations>(File.ReadAllText(path)) ?? new([]);
            return new((data.Bookmarks ?? []).Where(b => b is not null && b.SampleIndex >= 0 && b.Note is not null)
                .Take(SessionAnnotations.MaxBookmarks).Select(b => b with { Note = b.Note[..Math.Min(b.Note.Length, SessionAnnotations.MaxNoteLength)] }).ToArray());
        }
        catch (Exception ex) when (ex is JsonException or IOException) { throw new InvalidDataException("Session annotations could not be loaded.", ex); }
    }
    public static void Save(string sessionPath, IEnumerable<SessionBookmark> bookmarks)
    {
        var normalized = bookmarks.Where(b => b.SampleIndex >= 0 && b.Note is not null).Take(SessionAnnotations.MaxBookmarks)
            .Select(b => b with { Note = b.Note.Trim()[..Math.Min(b.Note.Trim().Length, SessionAnnotations.MaxNoteLength)] }).ToArray();
        var path = PathFor(sessionPath); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(new SessionAnnotations(normalized))); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
