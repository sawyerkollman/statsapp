namespace Stats.Core.Fans;

/// <summary>Persistent "a fan is under software control" flag so an unclean exit can be detected next launch.
/// Set() reports whether the flag is actually in place so the caller can retry a failed write instead of
/// latching "armed" and losing crash recovery for the rest of the session.</summary>
public interface IFanArmedMarker { bool Exists(); bool Set(); void Clear(); }

public sealed class FileFanArmedMarker : IFanArmedMarker
{
    private readonly string _path;
    public FileFanArmedMarker(string directory) => _path = Path.Combine(directory, "fans-armed");
    public bool Exists() { try { return File.Exists(_path); } catch { return false; } }
    public bool Set() { try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, DateTime.UtcNow.ToString("O")); return true; } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] fans-armed marker set failed: " + ex.Message); return false; } }
    public void Clear() { try { File.Delete(_path); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] fans-armed marker clear failed: " + ex.Message); } }
}
public sealed class NullFanArmedMarker : IFanArmedMarker { public bool Exists() => false; public bool Set() => false; public void Clear() { } }
