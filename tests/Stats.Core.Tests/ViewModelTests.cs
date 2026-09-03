using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;
using Stats.Core.ViewModels;

namespace Stats.Core.Tests;

public class ViewModelTests
{
    private static readonly MetricDefinition CpuTemp = new("cpu.temp", "Tctl", MetricGroup.Cpu, "CPU", "°C", "F1");
    private static readonly MetricDefinition CpuPpt = new("cpu.ppt", "CPU PPT", MetricGroup.Cpu, "CPU", "W", "F1");
    private static readonly MetricDefinition CpuLoad = new("cpu.load", "CPU Total", MetricGroup.Cpu, "CPU", "%");
    private static readonly MetricDefinition GpuClock = new("gpu.clock", "GPU Core", MetricGroup.Gpu, "GPU", "MHz");

    private static MetricStore NewStore() => new(new[] { CpuTemp, CpuPpt, CpuLoad, GpuClock });

    private static void Push(MetricStore store, float temp, float ppt, float clock, float load = 50f) =>
        store.Apply(new SensorSnapshot(new Dictionary<string, float?>
        { ["cpu.temp"] = temp, ["cpu.ppt"] = ppt, ["gpu.clock"] = clock, ["cpu.load"] = load }, DateTime.UtcNow));

    private static AppSettings Seeded() => new() { ThresholdRules = ThresholdDefaults.Rules() };

    // ---- tile ----

    [Fact]
    public void Tile_Refresh_FormatsCurrentMinAvgMax()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        Push(store, 45f, 71f, 2500f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], new AppSettings());
        tile.Refresh();
        Assert.Equal("45.0 °C", tile.CurrentText);
        Assert.Contains("min 40.0", tile.MinMaxText);
        Assert.Contains("avg 42.5", tile.MinMaxText);
        Assert.Contains("max 45.0", tile.MinMaxText);
        Assert.Equal(2, tile.HistoryValues.Length);
        Assert.Equal("2m", tile.HistoryWindowTag);
        Assert.Equal("°C", tile.Unit);
    }

    [Fact]
    public void Tile_HistoryWindowTag_ReflectsClampedCapacity_NotRequestedMinutes()
    {
        // 60 requested minutes at 0.5s would need 7200 samples; HistoryCapacity.Compute clamps to 3600, which at
        // 0.5s only covers 30 minutes — the tag must be honest about what the buffer actually holds.
        var history = new MetricHistory(HistoryCapacity.Compute(60, 0.5));
        var s = new AppSettings { HistoryWindowMinutes = 60, PollIntervalSeconds = 0.5 };
        var tile = new MetricTileViewModel(CpuTemp, history, s);
        tile.Refresh();
        Assert.Equal(3600, history.Capacity);
        Assert.Equal("30m", tile.HistoryWindowTag);
    }

    [Fact]
    public void Tile_WithLimit_ShowsPercentOfLimit_AndGaugeKind()
    {
        var store = NewStore();
        Push(store, 40f, 75f, 2400f);
        var s = new AppSettings { MetricLimits = { ["cpu.ppt"] = 150f } };
        var tile = new MetricTileViewModel(CpuPpt, store["cpu.ppt"], s);
        tile.Refresh();
        Assert.Equal("50% of 150.0 W", tile.LimitText);
        Assert.Equal(TileKind.Gauge, tile.Kind);   // explicit max → Auto resolves to Gauge
        Assert.Equal(150f, tile.Max);
        Assert.Equal(0.5f, tile.Fraction01, 3);
        Assert.Equal("150.0 W", tile.MaxText);
    }

    [Fact]
    public void Tile_WithZeroLimit_ShowsNoLimitText()
    {
        var store = NewStore();
        Push(store, 40f, 75f, 2400f);
        var s = new AppSettings { MetricLimits = { ["cpu.ppt"] = 0f } };
        var tile = new MetricTileViewModel(CpuPpt, store["cpu.ppt"], s);
        tile.Refresh();
        Assert.Equal("", tile.LimitText);
    }

    [Fact]
    public void Tile_FriendlyName_FromPrefs()
    {
        var store = NewStore();
        var s = new AppSettings();
        s.PrefFor("cpu.temp").Name = "CPU Temp";
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], s);
        tile.Refresh();
        Assert.Equal("CPU Temp", tile.DisplayName);
    }

    [Fact]
    public void Tile_Severity_FromThresholds()
    {
        var store = NewStore();
        Push(store, 93f, 70f, 2400f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], Seeded());
        tile.Refresh();
        Assert.Equal(Severity.Crit, tile.Severity);
    }

    // ---- accessibility (v1.8 §11b) ----

    [Fact]
    public void Tile_AutomationLabel_And_SeverityGlyph_Normal()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], Seeded());
        tile.Refresh();
        Assert.Equal(Severity.Normal, tile.Severity);
        Assert.Equal("", tile.SeverityGlyph);
        Assert.Equal("Tctl, 40.0 °C, Normal", tile.AutomationLabel);
    }

    [Fact]
    public void Tile_AutomationLabel_And_SeverityGlyph_Warn()
    {
        var store = NewStore();
        Push(store, 88f, 70f, 2400f); // between Warn(85) and Crit(92)
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], Seeded());
        tile.Refresh();
        Assert.Equal(Severity.Warn, tile.Severity);
        Assert.Equal("▲", tile.SeverityGlyph);
        Assert.Equal("Tctl, 88.0 °C, Warn", tile.AutomationLabel);
    }

    [Fact]
    public void Tile_AutomationLabel_And_SeverityGlyph_Crit()
    {
        var store = NewStore();
        Push(store, 93f, 70f, 2400f);
        var tile = new MetricTileViewModel(CpuTemp, store["cpu.temp"], Seeded());
        tile.Refresh();
        Assert.Equal(Severity.Crit, tile.Severity);
        Assert.Equal("‼", tile.SeverityGlyph);
        Assert.Equal("Tctl, 93.0 °C, Crit", tile.AutomationLabel);
    }

    [Fact]
    public void Tile_Kind_AutoRules()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        var s = new AppSettings();
        var load = new MetricTileViewModel(CpuLoad, store["cpu.load"], s); load.Refresh();
        var clock = new MetricTileViewModel(GpuClock, store["gpu.clock"], s); clock.Refresh();
        Assert.Equal(TileKind.Gauge, load.Kind);       // % → gauge with max 100
        Assert.Equal(100f, load.Max);
        Assert.Equal(TileKind.Sparkline, clock.Kind);  // no explicit max → sparkline
        Assert.Equal(2400f, clock.Max);                // derived from session max for Bar scale
    }

    [Fact]
    public void Tile_Kind_PercentOutsideCpuGpu_StaysSparkline()
    {
        var fan = new MetricDefinition("disk.used", "Used Space", MetricGroup.Storage, "SSD", "%");
        var store = new MetricStore(new[] { fan });
        store.Apply(new SensorSnapshot(new Dictionary<string, float?> { ["disk.used"] = 40f }, DateTime.UtcNow));
        var tile = new MetricTileViewModel(fan, store["disk.used"], new AppSettings());
        tile.Refresh();
        Assert.Equal(TileKind.Sparkline, tile.Kind);
    }

    [Fact]
    public void Tile_Kind_And_Size_PrefOverride()
    {
        var store = NewStore();
        Push(store, 40f, 70f, 2400f);
        var s = new AppSettings();
        s.PrefFor("gpu.clock").Kind = TileKind.Bar;
        s.PrefFor("gpu.clock").Size = TileSize.L;
        s.PrefFor("gpu.clock").Max = 3000f;
        var tile = new MetricTileViewModel(GpuClock, store["gpu.clock"], s);
        tile.Refresh();
        Assert.Equal(TileKind.Bar, tile.Kind);
        Assert.Equal(TileSize.L, tile.Size);
        Assert.Equal(3000f, tile.Max);
        Assert.Equal(0.8f, tile.Fraction01, 3);
    }

    // ---- dashboard (v1 behaviors still hold) ----

    [Fact]
    public void Dashboard_BuildsTilesOnlyForSelectedMetrics()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "gpu.clock", "cpu.temp" } };
        var vm = new DashboardViewModel(store, settings, () => { });
        Assert.Equal(new[] { "cpu.temp", "gpu.clock" }.OrderBy(x => x), vm.Tiles.Select(t => t.Definition.Id).OrderBy(x => x));
    }

    [Fact]
    public void Picker_Uncheck_RemovesTileAndUpdatesSettingsAndSaves()
    {
        var store = NewStore();
        var settings = new AppSettings { DashboardMetrics = { "cpu.temp" } };
        int saves = 0;
        var vm = new DashboardViewModel(store, settings, () => saves++);
        var item = vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp");

        item.IsChecked = false;
        Assert.Empty(vm.Tiles);
        Assert.Empty(settings.DashboardMetrics);
        Assert.Equal(1, saves);

        item.IsChecked = true;
        Assert.Single(vm.Tiles);
        Assert.Contains("cpu.temp", settings.DashboardMetrics);
    }

    [Fact]
    public void Picker_OverlayToggle_UpdatesOverlaySettingsAndRaisesEvent()
    {
        var store = NewStore();
        var settings = new AppSettings();
        var vm = new DashboardViewModel(store, settings, () => { });
        int raised = 0;
        vm.OverlayMetricsChanged += () => raised++;

        vm.PickerItems.Single(p => p.Definition.Id == "gpu.clock").IsOnOverlay = true;
        Assert.Contains("gpu.clock", settings.OverlayMetrics);
        Assert.Equal(1, raised);
    }

    // ---- overlay ----

    [Fact]
    public void Overlay_Rebuild_TracksSettings()
    {
        var store = NewStore();
        var settings = new AppSettings { OverlayMetrics = { "cpu.temp" } };
        var vm = new OverlayViewModel(store, settings);
        Assert.Single(vm.Tiles);
        settings.OverlayMetrics.Add("gpu.clock");
        vm.Rebuild();
        Assert.Equal(2, vm.Tiles.Count);
    }

    [Fact]
    public void Overlay_ApplyLayout_ReadsOrientationAndScale()
    {
        var store = NewStore();
        var settings = new AppSettings { OverlayOrientation = OverlayOrientation.Vertical, OverlayFontScale = 1.3 };
        var vm = new OverlayViewModel(store, settings);
        Assert.Equal(OverlayOrientation.Vertical, vm.Orientation);
        Assert.Equal(1.3, vm.FontScale);
        settings.OverlayOrientation = OverlayOrientation.Horizontal;
        vm.ApplyLayout();
        Assert.Equal(OverlayOrientation.Horizontal, vm.Orientation);
    }

    [Fact]
    public void Overlay_RefreshAll_UsesThresholdIndex_SameSeverityAsDirectEvaluator()
    {
        var store = NewStore();
        var settings = new AppSettings { OverlayMetrics = { "cpu.temp" }, ThresholdRules = ThresholdDefaults.Rules() };
        Push(store, 93f, 70f, 2400f);
        var vm = new OverlayViewModel(store, settings);
        vm.RefreshAll();
        Assert.Equal(Severity.Crit, vm.Tiles.Single().Severity);
    }

    // ---- history double-buffer (v1.8 §10 "History arrays") ----

    [Fact]
    public void Tile_HistoryValues_AlternatesReference_NeverSameTwiceInARow_OnceBufferIsFull()
    {
        var history = new MetricHistory(3);
        var s = new AppSettings();
        var tile = new MetricTileViewModel(CpuTemp, history, s);

        history.Add(1f); history.Add(2f); history.Add(3f); // fills the 3-slot ring buffer
        tile.Refresh();
        var first = tile.HistoryValues;
        history.Add(4f);
        tile.Refresh();
        var second = tile.HistoryValues;
        history.Add(5f);
        tile.Refresh();
        var third = tile.HistoryValues;

        Assert.NotSame(first, second);
        Assert.NotSame(second, third);
        Assert.Same(first, third); // alternates back to the same slot every other refresh
        Assert.Equal(new[] { 2f, 3f, 4f }, second);
        Assert.Equal(new[] { 3f, 4f, 5f }, third);
    }

    [Fact]
    public void Tile_HistoryValues_ContentAlwaysMatchesHistory_EvenDuringWarmup()
    {
        var history = new MetricHistory(5);
        var tile = new MetricTileViewModel(CpuTemp, history, new AppSettings());

        history.Add(1f);
        tile.Refresh();
        Assert.Equal(new[] { 1f }, tile.HistoryValues);

        history.Add(2f);
        tile.Refresh();
        Assert.Equal(new[] { 1f, 2f }, tile.HistoryValues);
    }

    // ---- picker CurrentText no-op (v1.8 §10 "Cheap extras") ----

    [Fact]
    public void Picker_CurrentText_UnchangedValue_DoesNotRaisePropertyChanged()
    {
        var store = NewStore();
        Push(store, 55f, 70f, 2400f);
        var settings = new AppSettings { DashboardMetrics = { "cpu.temp" } };
        var vm = new DashboardViewModel(store, settings, () => { });
        vm.IsPickerOpen = true;
        vm.RefreshAll(); // sets CurrentText the first time ("55.0 °C")

        var item = vm.PickerItems.Single(p => p.Definition.Id == "cpu.temp");
        int raised = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MetricPickerItem.CurrentText)) raised++; };

        vm.RefreshAll(); // same reading again — CurrentText string is identical
        Assert.Equal(0, raised);

        Push(store, 60f, 70f, 2400f);
        vm.RefreshAll(); // a genuinely new reading still raises it
        Assert.Equal(1, raised);
    }
}
