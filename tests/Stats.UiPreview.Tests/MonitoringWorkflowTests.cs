using Stats.Core.Alerts;
using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

public sealed class MonitoringWorkflowTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Build_ProvidesDeterministicLiveComparisonAndInterruptedAlertContext()
    {
        var first = PreviewComposition.Build("normal", Path.Combine(_root, "first"), ["alert-context"]);
        var second = PreviewComposition.Build("normal", Path.Combine(_root, "second"), []);

        Assert.NotEmpty(first.Comparison.Rows);
        Assert.Equal(first.Comparison.AxisEndUtc, first.Comparison.CursorTimeUtc);
        Assert.Equal(first.Comparison.Rows.Select(row => row.Values.Count), second.Comparison.Rows.Select(row => row.Values.Count));
        var alert = Assert.Single(first.AlertLog.ExportRecords());
        Assert.Equal(AlertState.Interrupted, alert.State);
        Assert.NotNull(alert.Context);
        Assert.Equal(alert.Context!.TimesUtc.Length, alert.Context.Values.Length);
    }

    [Fact]
    public void ProfilesAndSessionFixture_AreSavedLockedAndScopedToTempRoot()
    {
        var root = Path.Combine(_root, "workflow");
        var composition = PreviewComposition.Build("normal", root, ["profiles-saved", "layout-locked"]);
        var export = Path.Combine(root, "sessions", "preview.csv");

        Assert.NotEmpty(composition.Settings.LayoutProfiles);
        Assert.True(composition.Dashboard.IsLayoutLocked);
        Assert.NotEmpty(composition.Sessions.Rows);
        Assert.True(composition.Sessions.Export(export));
        Assert.True(File.Exists(export));
        Assert.StartsWith(root, Path.GetFullPath(composition.Sessions.FilePath!), StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(root, Path.GetFullPath(export), StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }
}
