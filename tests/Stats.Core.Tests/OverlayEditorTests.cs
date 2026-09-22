using Stats.Core.Settings;
using Stats.Core.Metrics;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public sealed class OverlayEditorTests
{
    [Fact]
    public void EdgeResizeStaysValidAndNewSelectionsRemainVisible()
    {
        var slot = new OverlayCanvasSlot(new() { MetricId = "cpu", X = 1800, Y = 1020, Width = 120, Height = 60 });
        slot.Width = 600; slot.Height = 300;
        Assert.True(slot.ToElement().IsValid());
        Assert.Equal(120, slot.Width); Assert.Equal(60, slot.Height);
        var cpu = new MetricDefinition("cpu", "CPU", MetricGroup.Cpu, "CPU", "%");
        var gpu = new MetricDefinition("gpu", "GPU", MetricGroup.Gpu, "GPU", "%");
        var settings = new AppSettings { OverlayMetrics = [cpu.Id, gpu.Id], OverlayCanvas = new() { Elements = [slot.ToElement()] } };
        var vm = new OverlayViewModel(new MetricStore([cpu, gpu]), settings);
        Assert.True(vm.HasAutomaticCanvas);
        Assert.Equal(2, vm.Tiles.Count);
        Assert.NotNull(settings.OverlayCanvas);
    }

    [Fact]
    public void PresetsFitMaximumCardCountWithoutOverlappingRows()
    {
        var definitions = Enumerable.Range(0, 32).Select(i => new MetricDefinition(i.ToString(), i.ToString(), MetricGroup.Cpu, "CPU", "%")).ToArray();
        var settings = new AppSettings { OverlayMetrics = definitions.Select(d => d.Id).ToList() };
        var vm = new OverlayEditorViewModel(settings, new MetricStore(definitions), () => { });
        foreach (var command in new[] { vm.CompactCommand, vm.AnalysisCommand, vm.SynthwaveCommand })
        {
            command.Execute(null); vm.ApplyCommand.Execute(null);
            Assert.Empty(vm.Error); Assert.True(settings.OverlayCanvas!.IsValid());
            Assert.Equal(32, vm.Slots.Select(s => (s.X, s.Y)).Distinct().Count());
        }
    }
    [Fact]
    public void Canvas_CloneAndNormalize_KeepOnlyBoundedGeometry()
    {
        var layout = new OverlayCanvasLayout { FixedNeonBorder = true, Elements = { new() { MetricId = "fps.avg", X = 8, Y = 16, Width = 120, Height = 60 } } };
        var clone = OverlayCanvasLayout.Normalize(layout);
        Assert.NotNull(clone); Assert.True(clone!.FixedNeonBorder); Assert.NotSame(layout, clone);
        layout.Elements[0].X = 99;
        Assert.Equal(8, clone.Elements[0].X);
        Assert.Null(OverlayCanvasLayout.Normalize(new OverlayCanvasLayout { Elements = { new() { MetricId = "bad", X = -1 } } }));
        Assert.Null(OverlayCanvasLayout.Normalize(new OverlayCanvasLayout { Elements = { null! } }));
    }

    [Fact]
    public void Scene_RestoresCanvasWithoutChangingAutomaticFields()
    {
        var canvas = new OverlayCanvasLayout { Elements = { new() { MetricId = "fps.avg", X = 0, Y = 0, Width = 120, Height = 60 } } };
        var scene = new DashboardScene { Name = "Game", OverlayCanvas = canvas };
        var settings = new AppSettings { OverlayMetrics = { "cpu.temp" } };
        scene.RestoreOverlay(settings);
        Assert.NotNull(settings.OverlayCanvas); Assert.NotSame(canvas, settings.OverlayCanvas);
        Assert.Equal(new[] { "cpu.temp" }, settings.OverlayMetrics);
    }

    [Fact]
    public void FpsOnly_IsSessionOnlyAndKeepsNoSelectionExplanation()
    {
        var fps = new MetricDefinition("fps.avg", "FPS", MetricGroup.Game, "Game", "fps");
        var cpu = new MetricDefinition("cpu.temp", "CPU", MetricGroup.Cpu, "CPU", "°C");
        var settings = new AppSettings { OverlayMetrics = { cpu.Id } };
        var vm = new OverlayViewModel(new MetricStore([fps, cpu]), settings);
        vm.ToggleFpsOnly();
        Assert.Empty(vm.Tiles);
        Assert.Equal("Select FPS in Overlay metrics to use FPS-only mode.", vm.FrameStatusText);
        Assert.Equal(new[] { cpu.Id }, settings.OverlayMetrics);
    }

    [Fact]
    public void Editor_GroupMoveUndoAndPortableImportRequireExplicitMapping()
    {
        var first = new MetricDefinition("cpu.a", "CPU A", MetricGroup.Cpu, "CPU", "%");
        var second = new MetricDefinition("cpu.b", "CPU B", MetricGroup.Cpu, "CPU", "%");
        var settings = new AppSettings { OverlayMetrics = [first.Id, second.Id] };
        var vm = new OverlayEditorViewModel(settings, new MetricStore([first, second]), () => { });
        vm.Select(vm.Slots[0], false); vm.Select(vm.Slots[1], true);
        var gap = vm.Slots[1].X - vm.Slots[0].X;
        vm.Nudge(vm.Slots[0], 8, 0);
        Assert.Equal(gap, vm.Slots[1].X - vm.Slots[0].X);
        vm.UndoCommand.Execute(null);
        Assert.Equal(24, vm.Slots[0].X);

        var json = vm.ExportDraft();
        vm.ImportDraft(json);
        Assert.Equal(2, vm.ImportMappings.Count);
        vm.ImportMappings[0].Selected = first;
        vm.ImportMappings[1].Selected = second;
        vm.ImportMappedCommand.Execute(null);
        Assert.Empty(vm.Error);
        Assert.Equal(2, vm.Slots.Count);
    }

    [Fact]
    public void Editor_FitAndBorderUndoAndInvalidImportAreDraftOnly()
    {
        var metric = new MetricDefinition("cpu", "CPU", MetricGroup.Cpu, "CPU", "%");
        var settings = new AppSettings { OverlayMetrics = [metric.Id] };
        var vm = new OverlayEditorViewModel(settings, new MetricStore([metric]), () => { });
        vm.FitToViewport(80, 80);
        Assert.Equal(.1, vm.Zoom);
        vm.ToggleNeonBorderCommand.Execute(null);
        Assert.True(vm.FixedNeonBorder);
        vm.UndoCommand.Execute(null);
        Assert.False(vm.FixedNeonBorder);
        vm.ImportDraft("{");
        Assert.Empty(vm.ImportMappings);
        Assert.Null(settings.OverlayCanvas);
    }
}
