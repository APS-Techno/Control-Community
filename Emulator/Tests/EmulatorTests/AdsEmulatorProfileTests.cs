using System.Xml.Linq;
using System.Xml.Serialization;
using APS.Emulator.Devices.S7;
using APS.SystemEmulator.Engine;
using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;
using EmulatorCore.Models;

namespace EmulatorTests;

public class AdsEmulatorProfileTests
{
    private static readonly string[] LanTypes = { "LanPowerSupply", "LanThrottleController", "LanRotaryDrive" };

    [Fact]
    public void CommunityEditionLoadsEveryAdsDevice()
    {
        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var missing = HwConfScan.ShortTypes(ConfigsDir.Path("ADS/HWConf.Emulator.xml")).Where(t => !registry.CanCreate(t));
        Assert.Empty(missing);
    }

    [Fact]
    public void EveryLanDeviceHasTcpSerialEntryOnItsPort()
    {
        var hw = XDocument.Load(ConfigsDir.Path("ADS/HWConf.Emulator.xml")).Root!;
        var lanDevices = hw.Elements()
            .Where(s => LanTypes.Any(t => ((string?)s.Attribute("hwtype") ?? "").EndsWith("." + t, StringComparison.Ordinal)))
            .SelectMany(s => s.Elements())
            .ToList();
        Assert.Equal(9, lanDevices.Count);

        var tcp = XDocument.Load(ConfigsDir.Path("ADS/EmulatorConfig.xml")).Descendants("TcpSerial")
            .ToDictionary(e => (string)e.Attribute("deviceId")!, e => (string)e.Attribute("port")!);
        foreach (var device in lanDevices)
        {
            Assert.True(tcp.TryGetValue(device.Value, out var port), $"no TcpSerial entry for {device.Value}");
            Assert.Equal((string?)device.Attribute("port"), port);
        }
    }

    [Fact]
    public void NoSerialEntries()
    {
        Assert.Empty(XDocument.Load(ConfigsDir.Path("ADS/EmulatorConfig.xml")).Descendants("Serial"));
    }

    [Fact]
    public void AdsEmulatorConfig_DefinesReactiveDepositionModel()
    {
        var ser = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = File.OpenRead(ConfigsDir.Path("ADS/EmulatorConfig.xml"));
        var cfg = (EmulatorConfig)ser.Deserialize(reader)!;

        var dep = cfg.Physics.Deposition;
        Assert.NotNull(dep);
        Assert.Equal("N21", dep!.ReactiveMfc);
        Assert.Equal(1.0f, dep.RatePerAmp, 4);
        Assert.Equal(0.00673f, dep.PoisonRate, 6);
        Assert.Equal(0.1f, dep.CleanRate, 4);
        Assert.Equal(0.2f, dep.PoisonedRateFraction, 4);
        Assert.Equal(100f, dep.PoisonedVoltageShift, 4);
    }

    [Fact]
    public void AdsEmulatorProfile_LoadsAndWiresTelemetrySensorsAndTurboBacking()
    {
        var ser = new XmlSerializer(typeof(EmulatorConfig));
        using var reader = File.OpenRead(ConfigsDir.Path("ADS/EmulatorConfig.xml"));
        var cfg = (EmulatorConfig)ser.Deserialize(reader)!;

        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var devices = new ConfigLoader(registry, cfg.Physics).LoadHWConfig(ConfigsDir.Path("ADS/HWConf.Emulator.xml"));
        registry.WireAll(devices);

        var sensors = devices.OfType<SensorEmulator>().ToDictionary(s => s.Id);
        Assert.True(sensors.ContainsKey("cm"));
        Assert.Equal("TP", sensors["pf"].PressureSourceId);
        Assert.True(sensors.ContainsKey("simtime"));
        Assert.True(sensors.ContainsKey("thick"));

        var tp = devices.OfType<TurboPumpEmulator>().Single(p => p.Id == "TP");
        Assert.Equal("SP1", tp.BackingPumpId);
    }
}
