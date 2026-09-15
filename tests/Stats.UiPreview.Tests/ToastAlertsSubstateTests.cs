using Stats.UiPreview.Fixtures;

namespace Stats.UiPreview.Tests;

/// <summary>Covers the toast-alerts harness substate (docs/superpowers/specs/2026-09-11-toast-alerts-design.md
/// "Preview harness"): <c>alerts-notify-off</c>. Built the same way <see cref="CompositionIsolationTests"/> and
/// <see cref="DashboardLayoutSubstateTests"/> cover their own settings-level substates — no window, straight
/// through <see cref="PreviewComposition.Build"/> — since this substate only flips a `SettingsViewModel` property
/// through its own write-through setter (no popup or visual-tree work).</summary>
public class ToastAlertsSubstateTests : IDisposable
{
    private readonly string _root;

    public ToastAlertsSubstateTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Stats.UiPreview.Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch (Exception) { /* best effort cleanup */ }
    }

    private string NewSubRoot([System.Runtime.CompilerServices.CallerMemberName] string name = "") =>
        Path.Combine(_root, name, Guid.NewGuid().ToString("N"));

    [Fact]
    public void AlertsNotifyOffSubstate_TurnsNotificationsOff_KeepsSkipWhenForeground_AndRecordsOneSave()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), new[] { "alerts-notify-off" });

        Assert.False(c.SettingsVm.AlertNotificationsEnabled);
        Assert.False(c.Settings.AlertNotificationsEnabled);
        // The sub-option's own setting is untouched — the checkbox reads greyed (IsEnabled bound to the master)
        // but its backing value is still the default.
        Assert.True(c.SettingsVm.AlertNotificationsSkipWhenForeground);
        Assert.True(c.Settings.AlertNotificationsSkipWhenForeground);
        Assert.Single(c.Commands.Entries, e => e.Contains("settings.save", StringComparison.Ordinal));
        Assert.True(c.Dashboard.IsPickerOpen);
        Assert.Equal(1, c.Dashboard.FlyoutTabIndex);
    }

    [Fact]
    public void NoSubstate_DefaultsBothNotificationSettingsOn()
    {
        var c = PreviewComposition.Build("normal", NewSubRoot(), Array.Empty<string>());

        Assert.True(c.SettingsVm.AlertNotificationsEnabled);
        Assert.True(c.SettingsVm.AlertNotificationsSkipWhenForeground);
        Assert.True(c.Settings.AlertNotificationsEnabled);
        Assert.True(c.Settings.AlertNotificationsSkipWhenForeground);
    }

    [Fact]
    public void SubstateCatalog_AcceptsAlertsNotifyOffForSettings_RejectsItForAlertsView()
    {
        SubstateCatalog.Validate("settings", new[] { "alerts-notify-off" }); // throws on failure
        Assert.Throws<ArgumentException>(() => SubstateCatalog.Validate("alerts", new[] { "alerts-notify-off" }));
    }
}
