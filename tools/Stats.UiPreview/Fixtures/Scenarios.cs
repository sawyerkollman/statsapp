using Stats.Core.Fans;
using Stats.Core.Frames;
using Stats.Core.Metrics;
using Stats.Core.Sensors;
using Stats.Core.Settings;

namespace Stats.UiPreview.Fixtures;

/// <summary>Builds each named scenario from PREVIEW_HARNESS.md's Scenarios table. Every number is an explicit
/// fixture value, never a device reading; series wander with a seeded <see cref="Random"/> (see
/// <see cref="TimeSeries.Seed"/>) around each target and land exactly on it for the final ("current") tick, so a
/// capture's displayed values are repeatable and match the illustrative numbers in the spec.</summary>
public static class Scenarios
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "normal", "dense", "thresholds", "missing", "empty", "fans", "settings", "gallery",
    };

    public static ScenarioFixture Build(string name)
    {
        ScenarioFixture fixture = name switch
        {
            "normal" => Normal(),
            "dense" => Dense(),
            "thresholds" => Thresholds(),
            "missing" => Missing(),
            "empty" => Empty(),
            "fans" => Fans(),
            "settings" => Settings(),
            "gallery" => Gallery(),
            _ => throw new ArgumentException($"Unknown scenario '{name}'. Valid: {string.Join(", ", Names)}", nameof(name)),
        };
        return WithDetailUnitExtra(fixture);
    }

    /// <summary>Every real unit in Stats is short (°C/%/W/V/A/RPM/B/s/MHz/GB — never more than 3 characters), so
    /// exercising the details view's "long-unit" substate needs one purpose-built metric with a longer unit,
    /// added to every scenario's definitions/history here rather than duplicated per scenario. Never part of any
    /// scenario's DashboardMetrics/OverlayMetrics selection — it only exists for the details view to pick up.</summary>
    private static ScenarioFixture WithDetailUnitExtra(ScenarioFixture f)
    {
        const string id = "details.longunit";
        var rng = new Random(TimeSeries.Seed + 999);
        var defs = f.Definitions.ToList();
        defs.Add(M(id, "Simulated Packet Rate", MetricGroup.Network, "Virtual Loopback Adapter (Preview Fixture)", "packets/s"));

        var b = new SnapshotBuilder(rng);
        b.Add(id, 84_532f, 1_200f, ticks: f.Ticks.Count);
        var extra = b.Build();

        var merged = new List<SensorSnapshot>(f.Ticks.Count);
        for (int i = 0; i < f.Ticks.Count; i++)
        {
            var values = new Dictionary<string, float?>(f.Ticks[i].Values) { [id] = extra[i].Values[id] };
            merged.Add(new SensorSnapshot(values, f.Ticks[i].TimestampUtc, f.Ticks[i].FailedBackends));
        }
        return f with { Definitions = defs, Ticks = merged };
    }

    private static MetricDefinition M(string id, string name, MetricGroup g, string hw, string unit, string fmt = "F0") =>
        new(id, name, g, hw, unit, fmt);

    // ---- normal ----

    private static ScenarioFixture Normal()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        const string gpuHw = "NVIDIA GeForce RTX 5070 Ti";

        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),
            M("cpu.load.total", "CPU Total", MetricGroup.Cpu, cpuHw, "%"),
            M("cpu.clock.avg", "CPU Clock", MetricGroup.Cpu, cpuHw, "MHz"),
            M("cpu.power.package", "Package Power", MetricGroup.Cpu, cpuHw, "W", "F1"),
            M("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1"),
            M("gpu.load.core", "GPU Load", MetricGroup.Gpu, gpuHw, "%"),
            M("gpu.power.package", "GPU Power", MetricGroup.Gpu, gpuHw, "W", "F1"),
            M("mem.load", "Memory", MetricGroup.Memory, "32 GB DDR5-6000", "%"),
            M("storage.ssd1.active", "SSD 1TB · Active Time", MetricGroup.Storage, "Samsung 990 Pro 1TB", "%"),
            M("net.eth.rx", "Ethernet · Download", MetricGroup.Network, "Realtek Gaming 2.5GbE", "B/s"),
        };
        defs.AddRange(FrameMetrics.Definitions);

        b.Add("cpu.temp.tctl", 62f, 1.5f).Add("cpu.load.total", 24f, 3f).Add("cpu.clock.avg", 4850f, 80f)
         .Add("cpu.power.package", 72f, 4f)
         .Add("gpu.temp.core", 56f, 1.5f).Add("gpu.load.core", 42f, 4f).Add("gpu.power.package", 135f, 6f)
         .Add("mem.load", 57.5f, 1f)
         .Add("storage.ssd1.active", 12f, 2f)
         .Add("net.eth.rx", 12_400_000f, 400_000f)
         .Add(FrameMetrics.FpsId, 144f, 3f).Add(FrameMetrics.LowId, 92f, 3f).Add(FrameMetrics.FrameTimeId, 6.9f, 0.3f);

        return new ScenarioFixture
        {
            Name = "normal",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = defs.Select(d => d.Id).ToList(),
            OverlayMetrics = new() { "cpu.temp.tctl", "gpu.temp.core", FrameMetrics.FpsId },
        };
    }

    // ---- dense ----

    private static ScenarioFixture Dense()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        var defs = new List<MetricDefinition>();
        var dash = new List<string>();
        var prefs = new Dictionary<string, TilePref>();
        var kinds = new[] { TileKind.Sparkline, TileKind.Gauge, TileKind.Bar, TileKind.Value, TileKind.Auto };
        var sizes = new[] { TileSize.S, TileSize.M, TileSize.L };
        int n = 0;

        void Tile(string id, string name, MetricGroup g, string hw, string unit, string fmt, float target, float noise)
        {
            defs.Add(M(id, name, g, hw, unit, fmt));
            b.Add(id, target, noise);
            dash.Add(id);
            prefs[id] = new TilePref { Kind = kinds[n % kinds.Length], Size = sizes[n % sizes.Length] };
            n++;
        }

        const string cpuHw = "AMD Ryzen 9 7950X3D";
        const string gpuHw = "NVIDIA GeForce RTX 5090 Founders Edition";

        // 16-core matrix (recognised automatically by CoreMatrixViewModel; not part of DashboardMetrics).
        var coreRng = new Random(TimeSeries.Seed + 1);
        for (int i = 1; i <= 16; i++)
        {
            defs.Add(M($"cpu.core{i}.load", $"Core #{i}", MetricGroup.Cpu, cpuHw, "%"));
            b.Add($"cpu.core{i}.load", 20f + (float)coreRng.NextDouble() * 70f, 5f);
            defs.Add(M($"cpu.core{i}.clock", $"Core #{i}", MetricGroup.Cpu, cpuHw, "MHz"));
            b.Add($"cpu.core{i}.clock", 3800f + (float)coreRng.NextDouble() * 1600f, 100f);
            if (i <= 8)
            {
                defs.Add(M($"cpu.core{i}.temp", $"Core #{i}", MetricGroup.Cpu, cpuHw, "°C", "F1"));
                b.Add($"cpu.core{i}.temp", 50f + (float)coreRng.NextDouble() * 35f, 2f);
            }
        }

        // CPU misc
        Tile("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1", 78f, 1.5f);
        Tile("cpu.power.package", "Package Power (PPT)", MetricGroup.Cpu, cpuHw, "W", "F1", 162.4f, 6f); // high wattage
        Tile("cpu.power.tdc", "TDC Current", MetricGroup.Cpu, cpuHw, "A", "F1", 84f, 3f);
        Tile("cpu.voltage.core", "Core Voltage (SVI3 TFN)", MetricGroup.Cpu, cpuHw, "V", "F3", 1.187f, 0.02f);

        // GPU
        Tile("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1", 71f, 1.5f);
        Tile("gpu.temp.hotspot", "GPU Hot Spot", MetricGroup.Gpu, gpuHw, "°C", "F1", 84f, 1.5f);
        Tile("gpu.load.core", "GPU Load", MetricGroup.Gpu, gpuHw, "%", "F0", 100f, 0f); // pinned 100%
        Tile("gpu.load.memctrl", "GPU Memory Controller Load", MetricGroup.Gpu, gpuHw, "%", "F0", 0f, 0f); // pinned 0%
        Tile("gpu.power.package", "GPU Power", MetricGroup.Gpu, gpuHw, "W", "F1", 452.7f, 8f); // high wattage
        Tile("gpu.clock.core", "GPU Core Clock", MetricGroup.Gpu, gpuHw, "MHz", "F0", 2610f, 40f);
        Tile("gpu.fan.rpm", "GPU Fan 1", MetricGroup.Gpu, gpuHw, "RPM", "F0", 1840f, 60f);

        // Memory
        Tile("mem.load", "Memory", MetricGroup.Memory, "64 GB DDR5-6400 CL30", "%", "F0", 61.2f, 2f);
        Tile("mem.used.gb", "Memory Used", MetricGroup.Memory, "64 GB DDR5-6400 CL30", "GB", "F1", 39.2f, 1f);

        // Storage — multiple disks, one long name, one pinned 100%.
        Tile("storage.ssd1.active", "SSD 1TB · Active Time", MetricGroup.Storage, "Samsung 990 Pro 2TB NVMe M.2", "%", "F0", 8f, 2f);
        Tile("storage.ssd1.temp", "SSD 1TB · Temperature", MetricGroup.Storage, "Samsung 990 Pro 2TB NVMe M.2", "°C", "F1", 44f, 1.5f);
        Tile("storage.ssd2.active", "SSD 2TB · Active Time", MetricGroup.Storage, "WD_BLACK SN850X 1000GB NVMe SSD", "%", "F0", 100f, 0f); // pinned 100%
        Tile("storage.hdd1.active", "Archive HDD · Active Time (Secondary Bulk Storage Array)", MetricGroup.Storage,
            "Seagate BarraCuda Pro 3.5-inch 8TB 7200RPM Enterprise Archive Drive", "%", "F0", 3f, 1f); // long name

        // Network — multiple adapters, one long-throughput value.
        Tile("net.eth.rx", "Ethernet · Download", MetricGroup.Network, "Realtek Gaming 2.5GbE Family Controller", "B/s", "F0", 1_284_500_000f, 20_000_000f); // long throughput
        Tile("net.eth.tx", "Ethernet · Upload", MetricGroup.Network, "Realtek Gaming 2.5GbE Family Controller", "B/s", "F0", 4_200_000f, 300_000f);
        Tile("net.wifi.rx", "Wi-Fi · Download", MetricGroup.Network, "Intel Wi-Fi 6E AX211 160MHz", "B/s", "F0", 820_000f, 60_000f);

        // Motherboard / Cooler
        Tile("mobo.temp.vrm", "VRM Temperature", MetricGroup.Motherboard, "ASUS ROG Crosshair X670E Hero", "°C", "F1", 58f, 2f);
        Tile("mobo.temp.chipset", "Chipset Temperature", MetricGroup.Motherboard, "ASUS ROG Crosshair X670E Hero", "°C", "F1", -4f, 1.5f); // negative temp
        Tile("mobo.voltage.12v", "+12V", MetricGroup.Motherboard, "ASUS ROG Crosshair X670E Hero", "V", "F2", 12.02f, 0.05f);
        Tile("cooler.pump.rpm", "Pump", MetricGroup.Cooler, "Corsair iCUE H150i Elite LCD XT", "RPM", "F0", 2950f, 40f);
        Tile("cooler.fan1.rpm", "Radiator Fan 1", MetricGroup.Cooler, "Corsair iCUE H150i Elite LCD XT", "RPM", "F0", 1120f, 30f);

        defs.AddRange(FrameMetrics.Definitions);
        b.Add(FrameMetrics.FpsId, 96f, 4f).Add(FrameMetrics.LowId, 61f, 3f).Add(FrameMetrics.FrameTimeId, 10.4f, 0.4f);
        dash.AddRange(new[] { FrameMetrics.FpsId, FrameMetrics.LowId, FrameMetrics.FrameTimeId });

        return new ScenarioFixture
        {
            Name = "dense",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = dash,
            OverlayMetrics = new() { "cpu.temp.tctl", "gpu.temp.core" },
            TilePrefs = prefs,
            MetricLimits = new() { ["cpu.power.package"] = 200f },
        };
    }

    // ---- thresholds ----

    private static ScenarioFixture Thresholds()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        const string gpuHw = "NVIDIA GeForce RTX 5070 Ti";

        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),   // Warn: 85 <= x < 92
            M("cpu.temp.core0", "Core #0", MetricGroup.Cpu, cpuHw, "°C", "F1"),    // Normal
            M("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1"),    // Crit: >= 88
        };
        defs.AddRange(FrameMetrics.Definitions); // fps.avg Warn (45), fps.low1 Crit (12) on the 30/15 override

        b.Add("cpu.temp.tctl", 88f, 0.3f)
         .Add("cpu.temp.core0", 54f, 1f)
         .Add("gpu.temp.core", 90f, 0.3f)
         .Add(FrameMetrics.FpsId, 45f, 0.5f)
         .Add(FrameMetrics.LowId, 12f, 0.5f)
         .Add(FrameMetrics.FrameTimeId, 22.2f, 0.3f);

        return new ScenarioFixture
        {
            Name = "thresholds",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = defs.Select(d => d.Id).ToList(),
            OverlayMetrics = new() { "cpu.temp.tctl", "gpu.temp.core", FrameMetrics.FpsId },
        };
    }

    // ---- missing ----

    private static ScenarioFixture Missing()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        const string gpuHw = "NVIDIA GeForce RTX 5070 Ti";

        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),
            M("cpu.load.total", "CPU Total", MetricGroup.Cpu, cpuHw, "%"),
            M("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1"),
            M("mem.load", "Memory", MetricGroup.Memory, "32 GB DDR5-6000", "%"),
        };
        defs.AddRange(FrameMetrics.Definitions);

        b.AddWithTrailingGap("cpu.temp.tctl", 61f, 1.5f, gapTicks: 12)
         .AddWithTrailingGap("cpu.load.total", 20f, 3f, gapTicks: 12)
         .AddWithTrailingGap("gpu.temp.core", 55f, 1.5f, gapTicks: 20)
         .Add("mem.load", 41f, 1f) // stays healthy — proves the gap is per-metric, not the whole store
         .AddWithTrailingGap(FrameMetrics.FpsId, 60f, 2f, gapTicks: TimeSeries.Ticks); // no foreground game at all

        return new ScenarioFixture
        {
            Name = "missing",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = new() { "cpu.temp.tctl", "cpu.load.total", "gpu.temp.core", "mem.load", FrameMetrics.FpsId },
            OverlayMetrics = new() { "cpu.temp.tctl" },
            Degraded = true,
            Health = new SensorHealthState(
                IsHealthy: false,
                ConsecutiveFailures: 5,
                FirstFailureLocalTime: TimeSeries.FixedTimeUtc.AddSeconds(-5).ToLocalTime(),
                LatestErrorFirstLine: "LibreHardwareMonitor: sensor read timed out",
                FailingBackends: new[] { "LibreHardwareMonitor" }),
            GameStatus = "No foreground game detected — bundled PresentMon runs only while a Game metric is selected.",
        };
    }

    // ---- empty ----

    private static ScenarioFixture Empty()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),
            M("cpu.load.total", "CPU Total", MetricGroup.Cpu, cpuHw, "%"),
        };
        defs.AddRange(FrameMetrics.Definitions);
        b.Add("cpu.temp.tctl", 45f, 1f).Add("cpu.load.total", 6f, 1f)
         .Add(FrameMetrics.FpsId, 0f, 0f).Add(FrameMetrics.LowId, 0f, 0f).Add(FrameMetrics.FrameTimeId, 0f, 0f);

        return new ScenarioFixture
        {
            Name = "empty",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = new(),
            OverlayMetrics = new(),
        };
    }

    // ---- fans ----

    private static ScenarioFixture Fans()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        const string gpuHw = "NVIDIA GeForce RTX 5070 Ti";

        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),
            M("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1"),
            M("fan.case1.rpm", "Fan #1", MetricGroup.Cooler, "ITE IT8696E Super I/O", "RPM"),
            M("fan.case1.percent", "Fan #1 Duty", MetricGroup.Cooler, "ITE IT8696E Super I/O", "%"),
            M("fan.case2.rpm", "Fan #2", MetricGroup.Cooler, "ITE IT8696E Super I/O", "RPM"),
            M("fan.case2.percent", "Fan #2 Duty", MetricGroup.Cooler, "ITE IT8696E Super I/O", "%"),
            M("fan.gpu.rpm", "GPU Fan 1", MetricGroup.Gpu, gpuHw, "RPM"),
            M("fan.gpu.percent", "GPU Fan 1 Duty", MetricGroup.Gpu, gpuHw, "%"),
            M("fan.pump.rpm", "Pump", MetricGroup.Cooler, "Corsair iCUE H150i Elite LCD XT", "RPM"),
            M("fan.pump.percent", "Pump Duty", MetricGroup.Cooler, "Corsair iCUE H150i Elite LCD XT", "%"),
        };

        b.Add("cpu.temp.tctl", 68f, 2f).Add("gpu.temp.core", 60f, 2f)
         .Add("fan.case1.rpm", 900f, 30f).Add("fan.case1.percent", 45f, 3f)
         .Add("fan.case2.rpm", 950f, 30f).Add("fan.case2.percent", 48f, 3f)
         .Add("fan.gpu.rpm", 1400f, 40f).Add("fan.gpu.percent", 55f, 3f)
         .Add("fan.pump.rpm", 2900f, 40f).Add("fan.pump.percent", 60f, 2f);

        var channels = new List<FanChannel>
        {
            new("/lpc/it8696e/0/control/0", "Fan #1", "ITE IT8696E Super I/O", "fan.case1.rpm", "fan.case1.percent", 0f, 100f),
            new("/lpc/it8696e/0/control/1", "Fan #2", "ITE IT8696E Super I/O", "fan.case2.rpm", "fan.case2.percent", 0f, 100f),
            new("/gpu-nvidia/0/control/0", "GPU Fan 1", gpuHw, "fan.gpu.rpm", "fan.gpu.percent", 30f, 100f),
            new("/usbhid/0/fan/0", "Pump", "Corsair iCUE H150i Elite LCD XT", "fan.pump.rpm", "fan.pump.percent", 0f, 100f),
        };

        return new ScenarioFixture
        {
            Name = "fans",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = new() { "cpu.temp.tctl", "gpu.temp.core" },
            FanChannels = channels,
        };
    }

    // ---- settings ----

    private static ScenarioFixture Settings()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        const string gpuHw = "NVIDIA GeForce RTX 5070 Ti";

        var defs = new List<MetricDefinition>
        {
            M("cpu.temp.tctl", "Tctl/Tdie", MetricGroup.Cpu, cpuHw, "°C", "F1"),
            M("cpu.load.total", "CPU Total", MetricGroup.Cpu, cpuHw, "%"),
            M("cpu.power.package", "Package Power", MetricGroup.Cpu, cpuHw, "W", "F1"),
            M("cpu.power.tdc", "TDC Current", MetricGroup.Cpu, cpuHw, "A", "F1"),
            M("gpu.temp.core", "GPU Core", MetricGroup.Gpu, gpuHw, "°C", "F1"),
            M("gpu.power.package", "GPU Power", MetricGroup.Gpu, gpuHw, "W", "F1"),
            M("mem.load", "Memory", MetricGroup.Memory, "32 GB DDR5-6000", "%"),
        };
        defs.AddRange(FrameMetrics.Definitions);

        b.Add("cpu.temp.tctl", 66f, 1.5f).Add("cpu.load.total", 30f, 3f)
         .Add("cpu.power.package", 88f, 4f).Add("cpu.power.tdc", 70f, 3f)
         .Add("gpu.temp.core", 58f, 1.5f).Add("gpu.power.package", 140f, 5f)
         .Add("mem.load", 49f, 1f)
         .Add(FrameMetrics.FpsId, 120f, 3f).Add(FrameMetrics.LowId, 80f, 3f).Add(FrameMetrics.FrameTimeId, 8.3f, 0.3f);

        return new ScenarioFixture
        {
            Name = "settings",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = defs.Select(d => d.Id).ToList(),
            OverlayMetrics = new() { "cpu.temp.tctl", "gpu.temp.core" },
            MetricLimits = new() { ["cpu.power.package"] = 142f },
            AccentHex = "#4A9EE0",
        };
    }

    // ---- gallery (V7 tile gallery: every kind x S/M/L + edge cases) ----

    private static ScenarioFixture Gallery()
    {
        var rng = new Random(TimeSeries.Seed);
        var b = new SnapshotBuilder(rng);
        const string cpuHw = "AMD Ryzen 9 7950X";
        var defs = new List<MetricDefinition>();
        var dash = new List<string>();
        var prefs = new Dictionary<string, TilePref>();
        var kinds = new[] { TileKind.Sparkline, TileKind.Gauge, TileKind.Bar, TileKind.Value };
        var sizes = new[] { TileSize.S, TileSize.M, TileSize.L };
        int n = 0;

        foreach (var kind in kinds)
        {
            foreach (var size in sizes)
            {
                var id = $"gallery.{kind}.{size}".ToLowerInvariant();
                defs.Add(M(id, $"{kind} {size}", MetricGroup.Cpu, cpuHw, "°C", "F1"));
                b.Add(id, 55f + n, 2f);
                dash.Add(id);
                prefs[id] = new TilePref { Kind = kind, Size = size };
                n++;
            }
        }

        // Long label.
        defs.Add(M("gallery.longname", "Long Name", MetricGroup.Gpu, "NVIDIA GeForce RTX 5070 Ti", "°C", "F1"));
        b.Add("gallery.longname", 62f, 1f);
        dash.Add("gallery.longname");
        prefs["gallery.longname"] = new TilePref { Kind = TileKind.Gauge, Size = TileSize.M,
            Name = "GPU Hot Spot Temperature Sensor (Secondary Die Region, Rear VRAM Cluster)" };

        // Unavailable value (gap on every tick).
        defs.Add(M("gallery.unavailable", "Unavailable", MetricGroup.Storage, "Removable USB SSD", "°C", "F1"));
        b.AddWithTrailingGap("gallery.unavailable", 40f, 1f, gapTicks: TimeSeries.Ticks);
        dash.Add("gallery.unavailable");
        prefs["gallery.unavailable"] = new TilePref { Kind = TileKind.Value, Size = TileSize.S };

        // Limit label (% of limit).
        defs.Add(M("gallery.limit", "Package Power", MetricGroup.Cpu, cpuHw, "W", "F1"));
        b.Add("gallery.limit", 118f, 3f);
        dash.Add("gallery.limit");
        prefs["gallery.limit"] = new TilePref { Kind = TileKind.Bar, Size = TileSize.M };

        // Inverted threshold (FPS-style, lower is worse) via an explicit per-tile override.
        defs.Add(M("gallery.inverted", "Simulated FPS", MetricGroup.Game, FrameMetrics.HardwareName, "fps"));
        b.Add("gallery.inverted", 28f, 1f); // below its own crit (15) is worse — 28 sits at Warn
        dash.Add("gallery.inverted");
        prefs["gallery.inverted"] = new TilePref { Kind = TileKind.Sparkline, Size = TileSize.L };

        return new ScenarioFixture
        {
            Name = "gallery",
            Definitions = defs,
            Ticks = b.Build(),
            DashboardMetrics = dash,
            TilePrefs = prefs,
            MetricLimits = new() { ["gallery.limit"] = 142f },
            ThresholdOverrides = new() { ["gallery.inverted"] = new ThresholdRule { Warn = 30, Crit = 15, LowerIsWorse = true } },
        };
    }
}
