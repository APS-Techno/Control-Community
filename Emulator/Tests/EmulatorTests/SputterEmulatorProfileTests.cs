using System.Xml.Serialization;
using APS.Emulator.Devices.S7;
using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;
using EmulatorCore.Models;

namespace EmulatorTests;

public class SputterEmulatorProfileTests
{
    [Fact]
    public void CommunityEditionLoadsEverySputterDevice()
    {
        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var missing = HwConfScan.ShortTypes(ConfigsDir.Path("Sputter/HWConf.Emulator.xml")).Where(t => !registry.CanCreate(t));
        Assert.Empty(missing);
    }

    [Fact]
    public void SputterEmulatorProfile_LoadsAndWiresTelemetrySensorsAndTurboBacking()
    {
        var ser = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = File.OpenRead(ConfigsDir.Path("Sputter/EmulatorConfig.xml"));
        var cfg = (EmulatorConfig)ser.Deserialize(reader)!;

        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var devices = new ConfigLoader(registry, cfg.Physics).LoadHWConfig(ConfigsDir.Path("Sputter/HWConf.Emulator.xml"));
        registry.WireAll(devices);

        var sensors = devices.OfType<SensorEmulator>().ToDictionary(s => s.Id);
        Assert.True(sensors.ContainsKey("cm"));
        Assert.Equal("TP", sensors["pf"].PressureSourceId);
        Assert.True(sensors.ContainsKey("simtime"));

        var tp = devices.OfType<TurboPumpEmulator>().Single(p => p.Id == "TP");
        Assert.Equal("SP1", tp.BackingPumpId);
    }
}
