using System.Text.RegularExpressions;

namespace APS.SystemEmulator.Setup;

public static partial class FirewallRuleSetup
{
    private const string Name = "Firewall rules";
    private const string RuleNamePrefix = "APS Emulator -";

    public static CheckRow Run(IReadOnlyList<int> requiredPorts)
    {
        try
        {
            string output = LoopbackAdapterSetup.RunNetsh(
                "advfirewall firewall show rule name=all verbose");
            var existing = ParseApsRulePorts(output);
            return Evaluate(requiredPorts, existing);
        }
        catch (Exception ex)
        {
            return new CheckRow(Name, CheckStatus.Unknown,
                $"Could not query firewall rules: {ex.Message}", [], Fixable: false);
        }
    }

    public static IReadOnlyList<int> ParseApsRulePorts(string showRulesOutput)
    {
        var ports = new List<int>();
        var blocks = Regex.Split(showRulesOutput, @"(?m)^Rule Name:\s*");
        foreach (var block in blocks)
        {
            if (!block.TrimStart().StartsWith(RuleNamePrefix)) continue;
            var match = LocalPortRegex().Match(block);
            if (match.Success && int.TryParse(match.Groups[1].Value, out int port))
                ports.Add(port);
        }
        return ports.Distinct().ToList();
    }

    public static CheckRow Evaluate(IReadOnlyList<int> required, IReadOnlyList<int> existing)
    {
        if (required.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                "No firewall rules required by current config.", [], Fixable: false);

        var have = new HashSet<int>(existing);
        var missing = required.Where(p => !have.Contains(p)).ToList();

        if (missing.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                $"All {required.Count} rule(s) present.", [], Fixable: false);

        var cmds = missing.Select(port =>
            $"netsh advfirewall firewall add rule name=\"{RuleNamePrefix} TCP {port}\" " +
            $"dir=in action=allow protocol=tcp localport={port}"
        ).ToList();

        return new CheckRow(Name, CheckStatus.Missing,
            $"Missing rules for TCP port(s): {string.Join(", ", missing)}",
            cmds, Fixable: true);
    }

    [GeneratedRegex(@"LocalPort:\s+(\d+)")]
    private static partial Regex LocalPortRegex();
}
