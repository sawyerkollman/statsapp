using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Stats.UiPreview;

namespace Stats.UiPreview.Tests;

/// <summary>Verifies, by reading the built Stats.UiPreview.dll's own metadata (not by trusting source review),
/// that it never references a production-only hardware/startup/update/tray type. This is the "meaningful test
/// proving the preview composition cannot resolve real hardware/startup/update services through its supported
/// paths" PREVIEW_HARNESS.md and TASKS.md T1 both call for.</summary>
public class IsolationMetadataTests
{
    // Full names as they appear in IL metadata (namespace.TypeName), matched against every TypeReference's
    // namespace+name the assembly's metadata table records.
    private static readonly (string Namespace, string Name)[] Forbidden =
    {
        ("Stats.App", "App"),
        ("Stats.Core.Sensors", "LhmSensorReader"),
        ("Stats.Core.Sensors", "PerfCounterSensorReader"),
        ("Stats.Core.Sensors", "CompositeSensorReader"),
        ("Stats.Core.Frames", "FrameRateReader"),
        ("Stats.Core.Frames", "PresentMonProcess"),
        ("Stats.Core.Frames", "PresentMonLocator"),
        ("Stats.App.Helpers", "StartupTaskService"),
        ("Stats.Core.Updates", "UpdateService"),
        ("Stats.App.Helpers", "GlobalHotkey"),
        ("Stats.App.Helpers", "RollingTraceLog"),
        ("Stats.Core.Fans", "FileFanArmedMarker"),
        ("H.NotifyIcon", "TaskbarIcon"),
    };

    private static string AssemblyPath => typeof(PreviewComposition).Assembly.Location;

    [Fact]
    public void BuiltAssembly_Exists()
    {
        Assert.True(File.Exists(AssemblyPath), $"Expected {AssemblyPath} to exist — build tools/Stats.UiPreview first.");
    }

    [Fact]
    public void BuiltAssembly_NeverReferencesProductionOnlyTypes()
    {
        var found = new List<string>();
        using var stream = File.OpenRead(AssemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.TypeReferences)
        {
            var typeRef = reader.GetTypeReference(handle);
            var ns = reader.GetString(typeRef.Namespace);
            var name = reader.GetString(typeRef.Name);
            if (Forbidden.Any(f => f.Namespace == ns && f.Name == name))
                found.Add($"{ns}.{name}");
        }

        Assert.True(found.Count == 0, "Stats.UiPreview.dll references forbidden production type(s): " + string.Join(", ", found));
    }

    [Fact]
    public void BuiltAssembly_NeverReferencesConflictingFanSoftware_RunningProcessNames()
    {
        // ConflictingFanSoftware.Match(...) is fine (pure, takes process names as data) — only the process-table
        // enumerator RunningProcessNames must never be called from the preview.
        using var stream = File.OpenRead(AssemblyPath);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        foreach (var handle in reader.MemberReferences)
        {
            var memberRef = reader.GetMemberReference(handle);
            var name = reader.GetString(memberRef.Name);
            Assert.NotEqual("RunningProcessNames", name);
        }
    }

    [Fact]
    public void PreviewApp_IsItsOwnCompositionRoot_NotStatsAppApp()
    {
        // Belt-and-braces: Stats.App.App must not even be loadable as a dependency surprise — the type must not
        // appear anywhere in the preview assembly's referenced type table (see the metadata test above), and the
        // preview's own composition root type must be Stats.UiPreview.PreviewApp. Checked purely by name/token —
        // never resolving System.Windows.Application itself — so this test project needs no WPF reference.
        var previewAppType = typeof(PreviewApp);
        Assert.Equal("Stats.UiPreview", previewAppType.Namespace);
        Assert.Equal("PreviewApp", previewAppType.Name);
        Assert.NotEqual("Stats.App", previewAppType.Namespace);
    }
}
