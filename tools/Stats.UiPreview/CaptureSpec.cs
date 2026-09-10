namespace Stats.UiPreview;

/// <summary>One capture request — the parsed form of the CLI options in docs/ui-polish/PREVIEW_HARNESS.md, and
/// also the shape of each entry in a --batch manifest (captures/baseline.json). Substates may be combined with
/// "+" (e.g. "on+curve") — see <see cref="SubstateList"/>.</summary>
public sealed class CaptureSpec
{
    public string Scenario { get; set; } = "normal";
    public string View { get; set; } = "dashboard";
    public string? Substate { get; set; }
    public string Theme { get; set; } = "Dark Amber";
    public string? Accent { get; set; }
    public double Width { get; set; } = 1180;
    public double Height { get; set; } = 720;
    public double UiScale { get; set; } = 1.0;
    public string? Output { get; set; }
    /// <summary>Compatibility fixture: a settings.json to copy into the run's temp root and load instead of the
    /// scenario's own generated AppSettings.</summary>
    public string? Settings { get; set; }
    public string Method { get; set; } = "screen";

    public IReadOnlyList<string> SubstateList =>
        string.IsNullOrWhiteSpace(Substate)
            ? Array.Empty<string>()
            : Substate.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public string CommandLine() =>
        "dotnet run --project tools/Stats.UiPreview -- " +
        $"--scenario {Scenario} --view {View}" +
        (string.IsNullOrEmpty(Substate) ? "" : $" --substate {Substate}") +
        $" --theme \"{Theme}\"" +
        (string.IsNullOrEmpty(Accent) ? "" : $" --accent {Accent}") +
        $" --width {Width:0.##} --height {Height:0.##} --ui-scale {UiScale:0.##}" +
        (string.IsNullOrEmpty(Settings) ? "" : $" --settings {Settings}") +
        $" --method {Method}" +
        (string.IsNullOrEmpty(Output) ? "" : $" --output {Output}");
}
