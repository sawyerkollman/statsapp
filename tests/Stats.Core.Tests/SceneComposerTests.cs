using Stats.Core.Metrics;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class SceneComposerTests
{
    [Fact]
    public void Export_OmitsMetricIdsAndDisplayNames()
    {
        var definition = new MetricDefinition("hardware.secret", "CPU Package", MetricGroup.Cpu, "AMD Ryzen", "°C");
        var settings = new AppSettings { DashboardMetrics = { definition.Id } };
        var vm = new SceneComposerViewModel(settings, [definition], () => { });
        vm.SaveCurrentCommand.Execute(null);
        var json = vm.ExportSelected();
        Assert.DoesNotContain(definition.Id, json); Assert.DoesNotContain(definition.DisplayName, json); Assert.DoesNotContain(definition.HardwareName, json);
    }

    [Fact]
    public void InvalidImport_DoesNotChangeScenes()
    {
        var settings = new AppSettings(); var vm = new SceneComposerViewModel(settings, [], () => { });
        Assert.Throws<InvalidDataException>(() => vm.Import("{\"Version\":2}"));
        Assert.Empty(vm.Scenes);
    }

    [Fact]
    public void SectionLabelsRoundTripWithoutPrivateNotesOrAutomaticMapping()
    {
        var metric = new MetricDefinition("secret", "CPU", MetricGroup.Cpu, "Machine", "%");
        var settings = new AppSettings { DashboardMetrics = { metric.Id } };
        var vm = new SceneComposerViewModel(settings, [metric], () => { });
        vm.SectionLabels.Single(r => r.Group == "Cpu").Label = "Engine room";
        vm.Caption = "Private experiment";
        vm.SaveCurrentCommand.Execute(null);
        var json = vm.ExportSelected();
        Assert.Contains("Engine room", json); Assert.DoesNotContain("Private experiment", json);
        vm.Import(json); Assert.Null(Assert.Single(vm.Mappings).Selected);
        vm.Mappings[0].Selected = metric; vm.ImportMappedCommand.Execute(null);
        Assert.Equal("Engine room", vm.SelectedScene!.SectionLabels["Cpu"]);
        Assert.Empty(vm.SelectedScene.Caption);
    }

    [Fact]
    public void LayoutRestore_PreservesDisplayThresholdAndFanPreferences()
    {
        var settings = new AppSettings(); settings.PrefFor("cpu").Name = "Quiet"; settings.PrefFor("cpu").Max = 90;
        var profile = LayoutProfile.Capture(settings, "scene"); profile.Restore(settings);
        Assert.Equal("Quiet", settings.PrefFor("cpu").Name); Assert.Equal(90, settings.PrefFor("cpu").Max);
    }
}
