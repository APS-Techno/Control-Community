using System.Xml.Linq;
using APS.SystemEmulator.Hosting;
using EmulatorCore.Engine;

namespace EmulatorTests;

public class ClusterEmulatorProfileTests
{
    [Fact]
    public void CommunityEditionLoadsEveryClusterDevice()
    {
        var registry = new DeviceRegistry(EmulatorEdition.Community.Registrations);
        var shortTypes = XDocument.Load(ConfigsDir.Path("Cluster/HWConf.Emulator.xml")).Root!.Elements()
            .Select(s => (string?)s.Attribute("hwtype"))
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t![(t.LastIndexOf('.') + 1)..])
            .Distinct();
        Assert.Empty(shortTypes.Where(t => !registry.CanCreate(t)));
    }

    [Fact]
    public void EveryLanDeviceHasTcpSerialEntryOnItsPort()
    {
        var tcp = XDocument.Load(ConfigsDir.Path("Cluster/EmulatorConfig.xml")).Descendants("TcpSerial")
            .ToDictionary(e => (string)e.Attribute("deviceId")!, e => e);
        foreach (var file in new[] { "Cluster/HWConf.xml", "Cluster/HWConf.Emulator.xml" })
        {
            var lan = XDocument.Load(ConfigsDir.Path(file)).Descendants()
                .Where(e => e.Attribute("host") is not null && e.Attribute("port") is not null)
                .ToList();
            Assert.Equal(7, lan.Count);
            foreach (var dev in lan)
            {
                var id = string.Concat(dev.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
                Assert.True(tcp.TryGetValue(id, out var entry), id);
                Assert.Equal((string?)dev.Attribute("port"), (string?)entry!.Attribute("port"));
                Assert.Equal("LfTerminated", (string?)entry.Attribute("framing"));
            }
        }
    }

    [Fact]
    public void S7ServersMatchPlcAddresses()
    {
        var servers = XDocument.Load(ConfigsDir.Path("Cluster/EmulatorConfig.xml")).Descendants("S7Server")
            .ToDictionary(e => (string)e.Attribute("plcId")!, e => (string?)e.Attribute("bindAddress"));
        var plcs = XDocument.Load(ConfigsDir.Path("Cluster/HWConf.Emulator.xml")).Descendants("plc")
            .ToDictionary(e => e.Value, e => (string?)e.Attribute("address"));
        Assert.Equal(plcs, servers);
    }
}
