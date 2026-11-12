using APS.SystemEmulator.Setup;

namespace EmulatorTests.Setup;

public class LoopbackAliasSetupTests
{
    [Fact]
    public void ReadLoopbackIPv4_includes_localhost()
    {
        // The NetworkInterface API is locale-proof, unlike the "IP Address:" label of
        // `netsh show config`. 127.0.0.1 is always present on the software loopback, so it's a
        // stable cross-locale invariant.
        var ips = LoopbackAliasSetup.ReadLoopbackIPv4();

        Assert.Contains("127.0.0.1", ips);
    }

    [Fact]
    public void Ok_when_all_required_ips_present()
    {
        string[] required = { "192.168.127.208", "192.168.128.209" };
        string[] existing = { "127.0.0.1", "192.168.127.208", "192.168.128.209" };

        var row = LoopbackAliasSetup.Evaluate(required, existing, interfaceName: "Loopback Pseudo-Interface 1");

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Empty(row.FixCommands);
    }

    [Fact]
    public void Missing_returns_add_address_commands_for_each_missing_ip()
    {
        string[] required = { "192.168.128.201", "192.168.127.208", "192.168.128.209" };
        string[] existing = { "127.0.0.1", "192.168.127.208" };

        var row = LoopbackAliasSetup.Evaluate(required, existing, interfaceName: "Loopback Pseudo-Interface 1");

        Assert.Equal(CheckStatus.Missing, row.Status);
        Assert.Equal(2, row.FixCommands.Count);
        Assert.Contains(row.FixCommands, c => c.Contains("192.168.128.201") && c.Contains("add address"));
        Assert.Contains(row.FixCommands, c => c.Contains("192.168.128.209"));
        Assert.All(row.FixCommands, c => Assert.Contains("\"Loopback Pseudo-Interface 1\"", c));
    }

    [Fact]
    public void Ok_with_empty_required_set()
    {
        var row = LoopbackAliasSetup.Evaluate(required: [], existing: [], interfaceName: "Loopback Pseudo-Interface 1");

        Assert.Equal(CheckStatus.Ok, row.Status);
        Assert.Contains("No loopback aliases required", row.Detail);
    }
}
