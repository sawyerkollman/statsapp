using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class DashboardViewModelTests
{
    private static readonly List<MetricDefinition> Defs = new()
    {
        new("cpu.temp", "Tctl", MetricGroup.Cpu, "Ryzen", "°C", "F1"),
        new("cpu.ppt", "CPU PPT", MetricGroup.Cpu, "Ryzen", "W", "F1"),
        new("cpu.l1", "CPU Core #1", MetricGroup.Cpu, "Ryzen", "%"),
        new("cpu.l2", "CPU Core #2", MetricGroup.Cpu, "Ryzen", "%"),
        new("gpu.clock", "GPU Core", MetricGroup.Gpu, "RTX", "MHz"),
        new("gpu.temp", "GPU Core", MetricGroup.Gpu, "RTX", "°C", "F1"),
        new("disk.c", "SSD-C · Activity", MetricGroup.Storage, "SSD-C", "%"),
    };

    private static (DashboardViewModel Vm, AppSettings S, MetricStore Store, Func<int> Saves) Make(params string[] selected)
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings { DashboardMetrics = selected.ToList() };
        int saves = 0;
        var vm = new DashboardViewModel(store, s, () => saves++);
        return (vm, s, store, () => saves);
    }

    [Fact]
    public void Sections_GroupedInGroupOrder_TilesFollowSettingsOrder()
    {
        var (vm, _, _, _) = Make("gpu.clock", "cpu.ppt", "disk.c", "cpu.temp");
        Assert.Equal(new[] { "Cpu", "Gpu", "Storage" }, vm.Sections.Select(x => x.Name));
        Assert.Equal(new[] { "cpu.ppt", "cpu.temp" }, vm.Sections[0].Tiles.Select(t => t.Definition.Id));
        Assert.Equal(4, vm.Tiles.Count); // flat collection mirrors all section tiles
    }

    [Fact]
    public void MoveTile_ReordersWithinGroup_PersistsAndSaves()
    {
        var (vm, s, _, saves) = Make("cpu.temp", "cpu.ppt", "gpu.clock");
        vm.MoveTile("cpu.ppt", "cpu.temp");
        Assert.Equal(new[] { "cpu.ppt", "cpu.temp", "gpu.clock" }, s.DashboardMetrics);
        Assert.Equal(new[] { "cpu.ppt", "cpu.temp" }, vm.Sections[0].Tiles.Select(t => t.Definition.Id));
        Assert.Equal(1, saves());
    }

    [Fact]
    public void MoveTile_AcrossGroupsOrUnknown_Ignored()
    {
        var (vm, s, _, saves) = Make("cpu.temp", "gpu.clock");
        vm.MoveTile("cpu.temp", "gpu.clock");
        vm.MoveTile("cpu.temp", "nope");
        Assert.Equal(new[] { "cpu.temp", "gpu.clock" }, s.DashboardMetrics);
        Assert.Equal(0, saves());
    }

    [Fact]
    public void SetTileKindSizeMax_WriteThroughPrefs_AndRefreshTile()
    {
        var (vm, s, _, saves) = Make("gpu.clock");
        vm.SetTileKind("gpu.clock", TileKind.Bar);
        vm.SetTileSize("gpu.clock", TileSize.L);
        vm.SetTileMax("gpu.clock", 3000f);
        Assert.Equal(TileKind.Bar, s.TilePrefs["gpu.clock"].Kind);
        Assert.Equal(TileSize.L, s.TilePrefs["gpu.clock"].Size);
        Assert.Equal(3000f, s.TilePrefs["gpu.clock"].Max);
        var tile = vm.Tiles.Single();
        Assert.Equal(TileKind.Bar, tile.Kind);
        Assert.Equal(TileSize.L, tile.Size);
        Assert.Equal(3, saves());
    }

    [Fact]
    public void RenameTile_UpdatesTileAndPickerItem_BlankClears()
    {
        var (vm, s, _, _) = Make("cpu.temp");
        vm.RenameTile("cpu.temp", "CPU Temp");
        Assert.Equal("CPU Temp", vm.Tiles.Single().DisplayName);
        Assert.Equal("CPU Temp", vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp").DisplayName);
        vm.RenameTile("cpu.temp", "   ");
        Assert.Equal("Tctl", vm.Tiles.Single().DisplayName);
        Assert.Null(s.TilePrefs["cpu.temp"].Name);
    }

    [Fact]
    public void RemoveTile_UnchecksPickerAndDropsTile()
    {
        var (vm, s, _, _) = Make("cpu.temp", "gpu.clock");
        vm.RemoveTile("cpu.temp");
        Assert.False(vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp").IsChecked);
        Assert.DoesNotContain("cpu.temp", s.DashboardMetrics);
        Assert.Single(vm.Tiles);
    }

    [Fact]
    public void CollapseAll_ExpandAll_ToggleGroup_PersistCollapsedGroups()
    {
        var (vm, s, _, _) = Make("cpu.temp", "gpu.clock");
        vm.CollapseAllCommand.Execute(null);
        Assert.All(vm.Sections, sec => Assert.False(sec.IsExpanded));
        Assert.Equal(new[] { "Cpu", "Gpu" }.OrderBy(x => x), s.CollapsedGroups.OrderBy(x => x));
        vm.ExpandAllCommand.Execute(null);
        Assert.Empty(s.CollapsedGroups);
        vm.Sections[1].IsExpanded = false;              // as the Expander would do via binding
        Assert.Equal(new[] { "Gpu" }, s.CollapsedGroups);
    }

    [Fact]
    public void Sections_RespectPersistedCollapsedGroups()
    {
        var store = new MetricStore(Defs);
        var s = new AppSettings { DashboardMetrics = { "cpu.temp", "gpu.clock" }, CollapsedGroups = { "Gpu" } };
        var vm = new DashboardViewModel(store, s, () => { });
        Assert.True(vm.Sections[0].IsExpanded);
        Assert.False(vm.Sections[1].IsExpanded);
    }

    [Fact]
    public void CoreMatrix_PresentInCpuSectionWhenEnabled_AbsentWhenDisabled()
    {
        var (vm, s, _, _) = Make("cpu.temp");
        Assert.NotNull(vm.CoreMatrix);
        Assert.Same(vm.CoreMatrix, vm.Sections[0].CoreMatrix);
        s.ShowCoreMatrix = false;
        vm.RebuildSections();
        Assert.Null(vm.CoreMatrix);
        Assert.Null(vm.Sections[0].CoreMatrix);
    }

    [Fact]
    public void CpuSection_ExistsForMatrixEvenWithNoCpuTiles()
    {
        var (vm, _, _, _) = Make("gpu.clock");
        Assert.Equal("Cpu", vm.Sections[0].Name);
        Assert.Empty(vm.Sections[0].Tiles);
        Assert.NotNull(vm.Sections[0].CoreMatrix);
    }

    [Fact]
    public void PickerMatches_FiltersByDisplayFriendlyOrHardwareName_CaseInsensitive()
    {
        var (vm, _, _, _) = Make();
        var ppt = vm.PickerItems.Single(p => p.Definition.Id == "cpu.ppt");
        var disk = vm.PickerItems.Single(p => p.Definition.Id == "disk.c");
        vm.PickerFilter = "";
        Assert.True(vm.PickerMatches(ppt));
        vm.PickerFilter = "PPT";
        Assert.True(vm.PickerMatches(ppt));
        Assert.False(vm.PickerMatches(disk));
        vm.PickerFilter = "ssd";
        Assert.True(vm.PickerMatches(disk));
        vm.RenameTile("cpu.ppt", "Package Power");
        vm.PickerFilter = "package";
        Assert.True(vm.PickerMatches(ppt));
    }

    [Fact]
    public void SelectAllInGroup_ChecksEveryItemInThatPickerGroup_SavesOnce()
    {
        var (vm, s, _, saves) = Make();
        var groupName = vm.PickerItems.First(p => p.Definition.Id == "cpu.temp").GroupName; // "Cpu — Ryzen"
        vm.SelectAllInGroup(groupName, true);
        Assert.Equal(4, s.DashboardMetrics.Count(id => id.StartsWith("cpu.")));
        Assert.DoesNotContain("gpu.clock", s.DashboardMetrics);
        Assert.Equal(1, saves());
        vm.SelectAllInGroup(groupName, false);
        Assert.Empty(s.DashboardMetrics);
    }

    [Fact]
    public void RefreshAll_UpdatesPickerLiveValues_OnlyWhenOpen()
    {
        var (vm, _, store, _) = Make("cpu.temp");
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["cpu.temp"] = 55f }, DateTime.UtcNow));
        vm.RefreshAll();
        Assert.Equal("", vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp").CurrentText);
        vm.IsPickerOpen = true;
        vm.RefreshAll();
        Assert.Equal("55.0 °C", vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp").CurrentText);
    }

    [Fact]
    public void OpenSettings_OpensFlyoutOnSettingsTab_AndEventsFire()
    {
        var (vm, _, _, _) = Make();
        int peaks = 0, changed = 0;
        vm.OpenPeaksRequested += () => peaks++;
        vm.DashboardMetricsChanged += () => changed++;
        vm.OpenSettingsCommand.Execute(null);
        Assert.True(vm.IsPickerOpen);
        Assert.Equal(1, vm.FlyoutTabIndex);
        vm.OpenPeaksCommand.Execute(null);
        Assert.Equal(1, peaks);
        vm.PickerItems.First().IsChecked = true;
        Assert.Equal(1, changed);
    }

    [Fact]
    public void SetTileThresholds_WritesOverride_RemovesWhenNullOrInvalid()
    {
        var (vm, s, _, saves) = Make("cpu.temp");
        vm.SetTileThresholds("cpu.temp", 70f, 80f);
        Assert.Equal(80f, s.ThresholdOverrides["cpu.temp"].Crit);
        vm.SetTileThresholds("cpu.temp", 90f, 80f); // warn >= crit → remove
        Assert.False(s.ThresholdOverrides.ContainsKey("cpu.temp"));
        vm.SetTileThresholds("cpu.temp", null, null);
        Assert.False(s.ThresholdOverrides.ContainsKey("cpu.temp"));
        Assert.Equal(3, saves());
    }

    [Fact]
    public void RenameTile_RaisesDashboardMetricsChanged()
    {
        var (vm, _, _, _) = Make("cpu.temp");
        int n = 0;
        vm.DashboardMetricsChanged += () => n++;
        vm.RenameTile("cpu.temp", "X");
        Assert.Equal(1, n);
    }

    [Fact]
    public void SetGroupStatus_SetsSectionText_AndSurvivesRebuild()
    {
        var defs = new List<MetricDefinition> { new("fps.avg", "FPS", MetricGroup.Game, "Foreground app", "fps"), new("cpu.l", "CPU", MetricGroup.Cpu, "CPU", "%") };
        var store = new MetricStore(defs);
        var vm = new DashboardViewModel(store, new AppSettings { DashboardMetrics = new() { "fps.avg", "cpu.l" } }, () => { });
        vm.SetGroupStatus(MetricGroup.Game, "PresentMon: access denied");
        var game = vm.Sections.Single(s => s.Group == MetricGroup.Game);
        Assert.Equal("PresentMon: access denied", game.StatusText);
        Assert.True(game.HasStatus);
        Assert.False(vm.Sections.Single(s => s.Group == MetricGroup.Cpu).HasStatus);
        vm.RebuildSections();
        Assert.Equal("PresentMon: access denied", vm.Sections.Single(s => s.Group == MetricGroup.Game).StatusText);
        vm.SetGroupStatus(MetricGroup.Game, null);
        Assert.False(vm.Sections.Single(s => s.Group == MetricGroup.Game).HasStatus);
        vm.SetGroupStatus(MetricGroup.Storage, "ignored"); // no section → no throw
    }
}
