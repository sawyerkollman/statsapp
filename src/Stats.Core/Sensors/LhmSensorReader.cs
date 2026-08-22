using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using Stats.Core.Metrics;

namespace Stats.Core.Sensors;

/// <summary>Wraps LibreHardwareMonitor. CPU temps/clocks/power need the PawnIO kernel driver (installed separately) plus admin.</summary>
public sealed class LhmSensorReader : ISensorReader
{
    private readonly Computer _computer;
    private readonly List<(ISensor Sensor, string MetricId)> _map = new();
    private List<MetricDefinition> _definitions = new();

    public LhmSensorReader()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
        };
        _computer.Open();
    }

    public string Name => "LibreHardwareMonitor";
    // LHM 0.9.6 reads CPU MSR/SMN only through PawnIO; without it the sensors exist but read 0.
    public bool IsDegraded => !PawnIo.IsInstalled;

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
        for (int i = 0; i < sensors.Count; i++)
        {
            if (defs[i] is not MetricDefinition def) continue;
            _map.Add((sensors[i], def.Id));
            _definitions.Add(def);
        }
        return _definitions;
    }

    public SensorSnapshot Read()
    {
        UpdateAll();
        var values = new Dictionary<string, float?>(_map.Count);
        foreach (var (sensor, id) in _map)
            values[id] = sensor.Value;
        return new SensorSnapshot(values, DateTime.UtcNow);
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
