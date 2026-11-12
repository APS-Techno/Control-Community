using APS.SystemEmulator.Setup;
using EmulatorCore.Models;

namespace EmulatorTests.Setup;

public class ConfigPrerequisitesTests
{
    [Fact]
    public void Collects_S7_and_SCPI_ips_from_protocols()
    {
        var cfg = new EmulatorConfig
        {
            Protocols =
            {
                new S7ServerConfig { BindAddress = "192.168.127.208", Port = 102 },
                new S7ServerConfig { BindAddress = "192.168.128.209", Port = 102 },
                new S7ServerConfig { BindAddress = "192.168.128.210", Port = 102 },
                new TcpSerialConfig { BindAddress = "192.168.128.201", Port = 5025 },
            }
        };

        var p = ConfigPrerequisites.From(cfg);

        Assert.Equal(
            new[] { "192.168.127.208", "192.168.128.201", "192.168.128.209", "192.168.128.210" },
            p.LoopbackIps.OrderBy(s => s).ToArray());
        Assert.Equal(new[] { 102, 5025 }, p.FirewallPorts.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Excludes_localhost_and_wildcard_binds()
    {
        var cfg = new EmulatorConfig
        {
            Protocols =
            {
                new S7ServerConfig { BindAddress = "127.0.0.1", Port = 102 },
                new S7ServerConfig { BindAddress = "0.0.0.0",   Port = 102 },
                new TcpSerialConfig { BindAddress = "localhost", Port = 5025 },
            }
        };

        var p = ConfigPrerequisites.From(cfg);

        Assert.Empty(p.LoopbackIps);
        Assert.Equal(new[] { 102, 5025 }, p.FirewallPorts.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Collects_com_port_names_from_serial_protocols()
    {
        var cfg = new EmulatorConfig
        {
            Protocols =
            {
                new SerialConfig { PortName = "COM103" },
                new SerialConfig { PortName = "COM121" },
            }
        };

        var p = ConfigPrerequisites.From(cfg);

        Assert.Equal(new[] { "COM103", "COM121" }, p.ComPorts.OrderBy(s => s).ToArray());
    }
}
