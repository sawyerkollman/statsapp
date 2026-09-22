using Stats.Core.Lab;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class GameAppearanceRestoreTests
{
    [Fact]
    public void RuleSetApplicationIsReportedEvenWhenSavingFails()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Stats-rules-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var vm = new LabViewModel(directory);
            vm.NamedRuleSets.Add(new() { Name = "Gaming", Rules = [new() { FirstMetricId = "cpu" }] });
            Directory.CreateDirectory(Path.Combine(directory, "lab-options.json")); // valid live swap, impossible file replacement
            Assert.True(vm.ApplyGameAlertSet("Gaming"));
            Assert.Equal("cpu", Assert.Single(vm.Rules).FirstMetricId);
            Assert.NotEmpty(vm.Error);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Fact]
    public void Defaults_RemainCompatibleAndDoNotTouchFans()
    {
        var option = Assert.Single(LabOptionsStore.Sanitize(new LabOptions { Games = [new()] }).Games);
        Assert.False(option.RestoreDesktopAfterGame);

        var settings = new AppSettings { FanControlEnabled = true };
        var restore = new GameAppearanceRestore();
        restore.CaptureBefore(settings, []);
        settings.ThemePreset = "Light";
        restore.Complete(settings, [], GameAppearanceDomains.Theme);
        Assert.True(restore.Restore(settings, []).Theme);
        Assert.True(settings.FanControlEnabled);
    }

    [Fact]
    public void Restore_UnchangedAutomaticDomainsReturnToTheirSnapshots()
    {
        var settings = new AppSettings
        {
            ThemePreset = "Dark Amber",
            DashboardMetrics = ["cpu"],
            OverlayMetrics = ["fps"],
            SceneSectionLabels = new() { ["Cpu"] = "Processor" },
            OverlayCanvas = new OverlayCanvasLayout { Elements = [new() { MetricId = "fps", Width = 180, Height = 90 }] },
        };
        settings.TilePrefs["cpu"] = new() { Kind = TileKind.Value, Size = TileSize.S };
        var rules = new List<CompoundRule> { new() { Id = Guid.NewGuid().ToString("N"), FirstMetricId = "cpu", SecondMetricId = "fps" } };
        var restore = new GameAppearanceRestore();
        restore.CaptureBefore(settings, rules);

        settings.ThemePreset = "Light";
        settings.DashboardMetrics[0] = "gpu";
        settings.SceneSectionLabels["Cpu"] = "Gaming CPU";
        settings.OverlayMetrics[0] = "frametime";
        settings.OverlayCanvas!.Elements[0].X = 12;
        rules[0].HoldSeconds = 30;
        restore.Complete(settings, rules, GameAppearanceDomains.Theme | GameAppearanceDomains.Layout | GameAppearanceDomains.Overlay | GameAppearanceDomains.CompoundRules);

        var result = restore.Restore(settings, rules);
        Assert.Equal(new GameAppearanceRestoreResult(true, true, true, true), result);
        Assert.Equal("Dark Amber", settings.ThemePreset);
        Assert.Equal("cpu", Assert.Single(settings.DashboardMetrics));
        Assert.Equal("Processor", settings.SceneSectionLabels["Cpu"]);
        Assert.Equal("fps", Assert.Single(settings.OverlayMetrics));
        Assert.Equal(0, Assert.Single(settings.OverlayCanvas!.Elements).X);
        Assert.Equal(5, Assert.Single(rules).HoldSeconds);
    }

    [Fact]
    public void Restore_StickyManualChangesWinAndOverlayDoesNotBlockLayout()
    {
        var settings = new AppSettings { ThemePreset = "Dark Amber", DashboardMetrics = ["cpu"], OverlayMetrics = ["fps"] };
        var rules = new List<CompoundRule> { new() { Id = Guid.NewGuid().ToString("N"), FirstMetricId = "cpu" } };
        var restore = new GameAppearanceRestore();
        restore.CaptureBefore(settings, rules);
        settings.ThemePreset = "Light"; settings.DashboardMetrics[0] = "gpu"; settings.OverlayMetrics[0] = "frametime"; rules[0].HoldSeconds = 20;
        restore.Complete(settings, rules, GameAppearanceDomains.Theme | GameAppearanceDomains.Layout | GameAppearanceDomains.Overlay | GameAppearanceDomains.CompoundRules);

        settings.ThemePreset = "Blue";
        settings.DashboardMetrics[0] = "memory";
        settings.OverlayMetrics[0] = "fps"; // Returning to an automatic value still records a manual change.
        rules[0].HoldSeconds = 45;
        restore.ObserveChanges(settings, rules);
        settings.DashboardMetrics[0] = "gpu";
        settings.OverlayMetrics[0] = "frametime";

        var result = restore.Restore(settings, rules);
        Assert.Equal(new GameAppearanceRestoreResult(false, false, false, false), result);
        Assert.Equal("Blue", settings.ThemePreset);
        Assert.Equal("gpu", Assert.Single(settings.DashboardMetrics));
        Assert.Equal("frametime", Assert.Single(settings.OverlayMetrics));
        Assert.Equal(45, Assert.Single(rules).HoldSeconds);
    }
}
