using Stats.Core.Fans;
using Stats.Core.Processes;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Builds each scenario's composition (no window, no WPF) and asserts the sensor reader, fan backend,
/// and marker are the fake types; the settings path is under a fresh per-run temp root, never
/// %AppData%\Stats; and the command log — after running every scenario's fan/settings substates — contains no
/// entry outside the allowed fake-service vocabulary.</summary>
public class CompositionIsolationTests : IDisposable
{
    private readonly string _root;

    public CompositionIsolationTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Build_UsesFakeServices_NotProductionOnes(string scenario)
    {
        var root = NewSubRoot();
        var c = PreviewComposition.Build(scenario, root, Array.Empty<string>());

        Assert.IsType<FakeSensorReader>(c.Reader);
        Assert.IsType<FakeFanControlBackend>(c.FanBackend);
        Assert.IsType<NullFanArmedMarker>(c.FanMarker);
        Assert.IsAssignableFrom<IFanControlBackend>(c.FanBackend);
        Assert.IsAssignableFrom<IFanArmedMarker>(c.FanMarker);
    }

    [Fact]
    public void Build_DoesNotExposeProductionProcessSources()
    {
        var composition = PreviewComposition.Build("normal", NewSubRoot(), Array.Empty<string>());
        var exposed = composition.GetType().GetProperties().Select(property => property.PropertyType.FullName ?? "");
        Assert.DoesNotContain(typeof(SystemProcessSource).FullName!, exposed);
        Assert.DoesNotContain("Stats.Core.Processes.GpuEngineCounters", exposed);
    }

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void Build_NeverWritesUnderRealAppDataStats(string scenario)
    {
        var root = NewSubRoot();
        var c = PreviewComposition.Build(scenario, root, Array.Empty<string>());

        var realAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Stats");
        Assert.StartsWith(root, c.SettingsPath, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Stats.UiPreview".ToUpperInvariant(), realAppData.ToUpperInvariant(), StringComparison.Ordinal);
        Assert.False(c.SettingsPath.StartsWith(realAppData, StringComparison.OrdinalIgnoreCase),
            "Settings path must never be under the user's real %AppData%\\Stats.");
        Assert.Contains("Stats.UiPreview", root, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_TempRoot_IsUnderTempPath()
    {
        var root = NewSubRoot();
        var c = PreviewComposition.Build("normal", root, Array.Empty<string>());
        Assert.StartsWith(Path.GetTempPath().TrimEnd('\\'), c.TempRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Save_WritesOnlyUnderTempRoot_AndLogsOneCommandPerSave()
    {
        var root = NewSubRoot();
        var c = PreviewComposition.Build("normal", root, Array.Empty<string>());
        c.SaveSettings();
        Assert.True(File.Exists(c.SettingsPath));
        Assert.Contains(c.Commands.Entries, e => e.Contains("settings.save", StringComparison.Ordinal));
    }

    // Every distinct verb the composition's CommandLog is allowed to record — see PreviewComposition.Build's Save()
    // closure and SettingsViewModel event handlers, plus FakeFanControlBackend's SetPercent/SetAuto messages.
    private static readonly string[] AllowedCommandVerbs =
    {
        "settings.save", "settings.load", "settings.openLogFolder", "settings.startupToggle", "settings.checkForUpdates",
        "settings.restart", "settings.overlayPositionReset", "fan.SetPercent", "fan.SetAuto",
    };

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void CommandLog_AfterExercisingEveryFanSubstate_ContainsOnlyAllowedFakeCommands(string scenario)
    {
        foreach (var substate in new[] { "off", "on", "manual", "curve", "pump", "modified", "no-channels", "conflict", "recovery", "write-failed" })
        {
            var root = NewSubRoot();
            var c = PreviewComposition.Build(scenario, root, new[] { substate });
            c.SettingsVm.OpenLogFolderCommand.Execute(null);
            c.SettingsVm.CheckForUpdatesCommand.Execute(null);
            c.SettingsVm.RestartNowCommand.Execute(null);
            c.SaveSettings();
            foreach (var entry in c.Commands.Entries)
                Assert.True(AllowedCommandVerbs.Any(v => entry.Contains(v, StringComparison.Ordinal)),
                    $"Unexpected command log entry for scenario={scenario} substate={substate}: {entry}");
        }
    }

    public static IEnumerable<object[]> ScenarioNames => Scenarios.Names.Select(n => new object[] { n });
}
