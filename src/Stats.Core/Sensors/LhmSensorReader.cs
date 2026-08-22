using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using Stats.Core.Fans;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Wraps LibreHardwareMonitor. CPU temps/clocks/power need the PawnIO kernel driver (installed separately) plus admin.
/// Also the fan-control backend: every sensor exposing an IControl becomes a FanChannel.</summary>
public sealed class LhmSensorReader : ISensorReader, IFanControlBackend
{
    private readonly Computer _computer;
    private readonly List<(ISensor Sensor, string MetricId)> _map = new();
    private List<MetricDefinition> _definitions = new();
    private readonly Dictionary<string, IControl> _controls = new();
    private List<FanChannel> _channels = new();

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsMotherboardEnabled = true,   // Super-I/O: board temps, fan headers + their PWM controls
            IsControllerEnabled = true,    // USB/HID fan & AIO controllers (e.g. MSI CoreLiquid)
        };
        _computer.Open();
    }

    public string Name => "LibreHardwareMonitor";
    // LHM 0.9.6 reads CPU MSR/SMN only through PawnIO; without it the sensors exist but read 0.
    public bool IsDegraded => !PawnIo.IsInstalled;

    public IReadOnlyList<FanChannel> Channels => _channels;

    public IReadOnlyList<MetricDefinition> Discover()
    {
        UpdateAll();

        var sensors = new List<ISensor>();
        foreach (var hardware in _computer.Hardware)
            Collect(hardware, sensors);

        var raws = sensors
            .Select(s => new RawSensor(
                s.Hardware.HardwareType.ToString(),
                s.Hardware.Name,
                s.SensorType.ToString(),
                s.Name))
            .ToList();

        var defs = SensorMapper.MapAll(raws);

        _map.Clear();
        _definitions = new List<MetricDefinition>();
        var idOf = new Dictionary<ISensor, string>();
        for (int i = 0; i < sensors.Count; i++)
        {
            if (defs[i] is not MetricDefinition def) continue;
            _map.Add((sensors[i], def.Id));
            _definitions.Add(def);
            idOf[sensors[i]] = def.Id;
        }

        DiscoverChannels(sensors, idOf);
        return _definitions;
    }

    /// <summary>Control-type sensors (ITE, NVIDIA) pair with the Fan sensor of the same hardware+index;
    /// Fan-type sensors that carry a control themselves (USB coolers) are their own RPM source.</summary>
    private void DiscoverChannels(List<ISensor> sensors, Dictionary<ISensor, string> idOf)
    {
        _controls.Clear();
        var channels = new List<FanChannel>();
        foreach (var s in sensors)
        {
            if (s.Control is not IControl ctl) continue;
            string id = s.Identifier.ToString();
            if (_controls.ContainsKey(id)) continue;
            ISensor? rpmSensor = s.SensorType == SensorType.Fan
                ? s
                : s.Hardware.Sensors.FirstOrDefault(o => o.SensorType == SensorType.Fan && o.Index == s.Index);
            ISensor? pctSensor = s.SensorType == SensorType.Control ? s : null;
            _controls[id] = ctl;
            channels.Add(new FanChannel(
                Id: id,
                Name: s.Name,
                Device: s.Hardware.Name,
                RpmMetricId: rpmSensor is not null && idOf.TryGetValue(rpmSensor, out var rid) ? rid : null,
                PercentMetricId: pctSensor is not null && idOf.TryGetValue(pctSensor, out var pid) ? pid : null,
                MinPercent: ctl.MinSoftwareValue,
                MaxPercent: ctl.MaxSoftwareValue));
        }
        _channels = channels;
    }

    public SensorSnapshot Read()
    {
        UpdateAll();
        var values = new Dictionary<string, float?>(_map.Count);
        foreach (var (sensor, id) in _map)
            values[id] = sensor.Value;
        return new SensorSnapshot(values, DateTime.UtcNow);
    }

    public void SetPercent(string channelId, float percent)
    {
        var ctl = _controls[channelId];
        ctl.SetSoftware(Math.Clamp(percent, ctl.MinSoftwareValue, ctl.MaxSoftwareValue));
    }

    public void SetAuto(string channelId)
    {
        if (_controls.TryGetValue(channelId, out var ctl)) ctl.SetDefault();
    }

    private void UpdateAll()
    {
        foreach (var hardware in _computer.Hardware)
            UpdateRecursive(hardware);
    }

    private static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    private static void Collect(IHardware hardware, List<ISensor> acc)
    {
        acc.AddRange(hardware.Sensors);
        foreach (var sub in hardware.SubHardware)
            Collect(sub, acc);
    }

    public void Dispose() => _computer.Close();
}
