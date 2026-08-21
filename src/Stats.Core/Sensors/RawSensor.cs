namespace Stats.Core.Sensors;

/// <summary>Plain-string description of one discovered sensor. Keeps SensorMapper free of LHM types.</summary>
public sealed record RawSensor(string HardwareType, string HardwareName, string SensorType, string SensorName);
