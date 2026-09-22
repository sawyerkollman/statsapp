using System.Text.Json;
namespace Stats.Core.Lab;
public sealed partial class TuningNotebookEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string _name = "";
    public string Workload { get; set; } = "";
    public string GraphicsSettings { get; set; } = "";
    public string CoreOffsetMHz { get; set; } = "";
    public string MemoryOffsetMHz { get; set; } = "";
    public string Voltage { get; set; } = "";
    public string PowerLimit { get; set; } = "";
    public string StabilityNotes { get; set; } = "";
    public string? RecordingA { get; set; }
    public string? RecordingB { get; set; }
}
public sealed class TuningNotebook
{
    public List<TuningNotebookEntry> Entries { get; set; } = new();
    public static TuningNotebook Load(string directory) { var path = System.IO.Path.Combine(directory, "tuning-notebook.json"); if (!File.Exists(path)) return new(); if (new FileInfo(path).Length > 512_000) throw new InvalidDataException("Notebook exceeds 512 KB."); return Normalize(JsonSerializer.Deserialize<TuningNotebook>(File.ReadAllText(path)) ?? new()); }
    public static void Save(string directory, TuningNotebook notebook) { var json = JsonSerializer.Serialize(Normalize(notebook)); if (System.Text.Encoding.UTF8.GetByteCount(json) > 512_000) throw new InvalidDataException("Notebook exceeds 512 KB."); var path = System.IO.Path.Combine(directory, "tuning-notebook.json"); var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp"; try { Directory.CreateDirectory(directory); File.WriteAllText(temp, json); File.Move(temp, path, true); } finally { if (File.Exists(temp)) File.Delete(temp); } }
    public static TuningNotebook Normalize(TuningNotebook notebook) { notebook.Entries = (notebook.Entries ?? []).Where(x => x is not null).Take(100).Select(x => new TuningNotebookEntry { Name = Text(x.Name), Workload = Text(x.Workload), GraphicsSettings = Text(x.GraphicsSettings), CoreOffsetMHz = Text(x.CoreOffsetMHz), MemoryOffsetMHz = Text(x.MemoryOffsetMHz), Voltage = Text(x.Voltage), PowerLimit = Text(x.PowerLimit), StabilityNotes = Text(x.StabilityNotes), RecordingA = Path(x.RecordingA), RecordingB = Path(x.RecordingB) }).ToList(); return notebook; }
    private static string Text(string? value) { var text = (value ?? "").Trim(); if (text.Length > 1000) throw new InvalidDataException("Notebook fields are limited to 1000 characters."); return text; }
    private static string? Path(string? value) { if (string.IsNullOrWhiteSpace(value)) return null; var path = System.IO.Path.GetFullPath(value); if (path.Length > 260 || !path.EndsWith(".stats-session.jsonl", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Recording links must be local .stats-session.jsonl files."); return path; }
}
