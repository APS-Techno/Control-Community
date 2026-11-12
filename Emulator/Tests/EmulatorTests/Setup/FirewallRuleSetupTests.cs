using APS.SystemEmulator.Setup;

namespace EmulatorTests.Setup;

public class FirewallRuleSetupTests
{
    [Fact]
    public void Parses_existing_rule_ports_by_name_prefix()
    {
        const string output = """
            Rule Name:                            APS Emulator - S7 (TCP 102)
            ----------------------------------------------------------------------
            Enabled:                              Yes
            Direction:                            In
            Protocol:                             TCP
            LocalPort:                            102

            Rule Name:                            Some Other Rule
            ----------------------------------------------------------------------
            Enabled:                              Yes
            LocalPort:                            80

            Rule Name:                            APS Emulator - SCPI (TCP 5025)
            ----------------------------------------------------------------------
            Enabled:                              Yes
            LocalPort:                            5025
            """;

        var ports = FirewallRuleSetup.ParseApsRulePorts(output);

        Assert.Equal(new[] { 102, 5025 }, ports.OrderBy(n => n).ToArray());
    }

    [Fact]
    public void Ok_when_all_required_ports_have_rules()
    {
        int[] required = { 102, 5025 };
        int[] existing = { 102, 5025 };

        var row = FirewallRuleSetup.Evaluate(required, existing);

        Assert.Equal(CheckStatus.Ok, row.Status);
    }

    [Fact]
    public void Missing_emits_add_rule_commands_with_prefixed_name()
    {
        int[] required = { 102, 5025 };
        int[] existing = { 102 };

        var row = FirewallRuleSetup.Evaluate(required, existing);

        Assert.Equal(CheckStatus.Missing, row.Status);
        Assert.Single(row.FixCommands);
        Assert.Contains("APS Emulator -", row.FixCommands[0]);
        Assert.Contains("localport=5025", row.FixCommands[0]);
        Assert.Contains("protocol=tcp", row.FixCommands[0]);
        Assert.Contains("dir=in", row.FixCommands[0]);
    }
}
