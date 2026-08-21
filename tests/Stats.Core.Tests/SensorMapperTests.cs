using Stats.Core.Metrics;
using Stats.Core.Sensors;

namespace Stats.Core.Tests;

public class SensorMapperTests
{
    [Fact]
    public void Map_CpuTemperature_ProducesCpuGroupCelsiusAndSlugId()
    {
        var def = SensorMapper.Map(new RawSensor("Cpu", "AMD Ryzen 7 9800X3D", "Temperature", "Core (Tctl/Tdie)"));
        Assert.NotNull(def);
        Assert.Equal(MetricGroup.Cpu, def!.Group);
        Assert.Equal("°C", def.Unit);
        Assert.Equal("cpu.amd-ryzen-7-9800x3d.temperature.core-tctl-tdie", def.Id);
        Assert.Equal("Core (Tctl/Tdie)", def.DisplayName); // single-instance group: no hardware prefix
    }

    [Fact]
    public void Map_NvidiaGpuClock_ProducesGpuGroupWithHardwarePrefixedName()
    {
        var def = SensorMapper.Map(new RawSensor("GpuNvidia", "NVIDIA GeForce RTX 5070 Ti", "Clock", "GPU Core"));
        Assert.NotNull(def);
        Assert.Equal(MetricGroup.Gpu, def!.Group);
        Assert.Equal("MHz", def.Unit);
        Assert.Contains("RTX 5070 Ti", def.DisplayName); // multi-instance group keeps hardware name visible
    }

    [Fact]
    public void Map_MotherboardSensor_ReturnsNull()
    {
        Assert.Null(SensorMapper.Map(new RawSensor("Motherboard", "X870", "Temperature", "System")));
    }

    [Fact]
    public void Map_UnknownSensorType_ReturnsNull()
    {
        Assert.Null(SensorMapper.Map(new RawSensor("Cpu", "X", "Factor", "Weird")));
    }

    [Theory]
    [InlineData("Temperature", "°C")]
    [InlineData("Clock", "MHz")]
    [InlineData("Load", "%")]
    [InlineData("Power", "W")]
    [InlineData("Voltage", "V")]
    [InlineData("Current", "A")]
    [InlineData("Fan", "RPM")]
    [InlineData("Throughput", "B/s")]
    [InlineData("Data", "GB")]
    [InlineData("SmallData", "MB")]
    [InlineData("Level", "%")]
    [InlineData("Control", "%")]
    public void Map_SensorTypeUnits(string sensorType, string expectedUnit)
    {
        var def = SensorMapper.Map(new RawSensor("Cpu", "X", sensorType, "S"));
        Assert.Equal(expectedUnit, def!.Unit);
    }

    [Fact]
    public void MapAll_DuplicateIds_GetNumericSuffixes()
    {
        var raw = new RawSensor("Storage", "Samsung SSD", "Load", "Used Space");
        var defs = SensorMapper.MapAll(new[] { raw, raw, raw });
        Assert.Equal(3, defs.Count);
        Assert.Equal(defs[0]!.Id + "-2", defs[1]!.Id);
        Assert.Equal(defs[0]!.Id + "-3", defs[2]!.Id);
    }

    [Fact]
    public void MapAll_PreservesInputOrderAndNullsForSkipped()
    {
        var defs = SensorMapper.MapAll(new[]
        {
            new RawSensor("Cpu", "X", "Temperature", "A"),
            new RawSensor("Motherboard", "X", "Temperature", "B"),
        });
        Assert.NotNull(defs[0]);
        Assert.Null(defs[1]);
    }

    [Fact]
    public void Slug_LowercasesAndCollapsesNonAlphanumerics()
    {
        Assert.Equal("core-tctl-tdie", SensorMapper.Slug("Core (Tctl/Tdie)"));
        Assert.Equal("gpu-core", SensorMapper.Slug("GPU Core"));
        Assert.Equal("d", SensorMapper.Slug("(((D:)))"));
    }
}
