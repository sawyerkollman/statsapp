namespace Stats.Core.Fans;

/// <summary>Persistent "a fan is under software control" flag so an unclean exit can be detected next launch.</summary>
public interface IFanArmedMarker { bool Exists(); void Set(); void Clear(); }

public sealed class FileFanArmedMarker : IFanArmedMarker
{
    private readonly string _path;
    public FileFanArmedMarker(string directory) => _path = Path.Combine(directory, "fans-armed");
    public bool Exists() { try { return File.Exists(_path); } catch { return false; } }
    public void Set() { try { Directory.CreateDirectory(Path.GetDirectoryName(_path)!); File.WriteAllText(_path, DateTime.UtcNow.ToString("O")); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] fans-armed marker set failed: " + ex.Message); } }
    public void Clear() { try { File.Delete(_path); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine("[Stats] fans-armed marker clear failed: " + ex.Message); } }
}
public sealed class NullFanArmedMarker : IFanArmedMarker { public bool Exists() => false; public void Set() { } public void Clear() { } }
