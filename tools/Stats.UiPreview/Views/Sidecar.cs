using System.Text.Json;
using System.Text.Json.Serialization;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Views;

/// <summary>The JSON sidecar PREVIEW_HARNESS.md requires next to every PNG.</summary>
public sealed class Sidecar
{
    public string HarnessVersion { get; init; } = "1.0.0";
    public string SourceCommit { get; init; } = "";
    public string DirtyDiffIdentity { get; init; } = "";
    public string Scenario { get; init; } = "";
    public string? Substate { get; init; }
    public string View { get; init; } = "";
    public string Theme { get; init; } = "";
    public string? Accent { get; init; }
    public double LogicalWidth { get; init; }
    public double LogicalHeight { get; init; }
    public int PhysicalWidth { get; init; }
    public int PhysicalHeight { get; init; }
    public double DpiX { get; init; }
    public double DpiY { get; init; }
    public double UiScale { get; init; }
    public string Culture { get; init; } = "";
    public string TimeZoneId { get; init; } = "";
    public string FixtureTimeUtc { get; init; } = "";
    public int FixtureSeed { get; init; }
    public string LaunchCommand { get; init; } = "";
    public string ScreenshotMethod { get; init; } = "";
    public List<string> Warnings { get; init; } = new();
    public List<SimulatedService> SimulatedServices { get; init; } = new();

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void WriteTo(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, Options));

    public static readonly IReadOnlyList<SimulatedService> AllSimulated = new[]
    {
        new SimulatedService("ISensorReader", "FakeSensorReader — no LibreHardwareMonitor/PawnIO/perf-counter access"),
        new SimulatedService("IFanControlBackend", "FakeFanControlBackend — records intended writes, never reaches hardware"),
        new SimulatedService("IFanArmedMarker", "NullFanArmedMarker — no crash-recovery file written"),
        new SimulatedService("Startup task", "SettingsViewModel.StartupToggleRequested handled in-process, no schtasks call"),
        new SimulatedService("Update checker", "SettingsViewModel.CheckForUpdatesRequested handled in-process, no GitHub call"),
        new SimulatedService("Settings persistence", "SettingsService pointed at a fresh per-run %TEMP%\\Stats.UiPreview\\<run-id> root"),
        new SimulatedService("Global hotkey / tray / PresentMon", "never constructed by the preview host"),
    };
}

public sealed record SimulatedService(string Name, string Detail)
{
    public bool Simulated => true;
}
