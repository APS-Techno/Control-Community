using System.Xml.Linq;
using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;

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
}
