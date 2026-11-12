using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace APS.SystemEmulator.Setup;

public static class LoopbackAliasSetup
{
    private const string Name = "Loopback IP aliases";
    public const string DefaultInterfaceName = "Loopback Pseudo-Interface 1";

    public static CheckRow Run(IReadOnlyList<string> requiredIps, string? interfaceName = null)
    {
        try
        {
            // Not parsed from `netsh show config`: its "IP Address:" label and the interface
            // name are translated on localized Windows.
            var lb = LoopbackAdapterSetup.FindLoopback();
            interfaceName ??= lb?.Name ?? DefaultInterfaceName;
            var existing = ReadIPv4(lb);
            return Evaluate(requiredIps, existing, interfaceName);
        }
        catch (Exception ex)
        {
            return new CheckRow(Name, CheckStatus.Unknown,
                $"Could not query loopback addresses: {ex.Message}", [], Fixable: false);
        }
    }

    /// <summary>The loopback interface's assigned IPv4 addresses (always includes 127.0.0.1).</summary>
    public static IReadOnlyList<string> ReadLoopbackIPv4() => ReadIPv4(LoopbackAdapterSetup.FindLoopback());

    private static IReadOnlyList<string> ReadIPv4(NetworkInterface? ni)
    {
        if (ni is null) return [];
        return ni.GetIPProperties().UnicastAddresses
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString())
            .ToList();
    }

    public static CheckRow Evaluate(
        IReadOnlyList<string> required,
        IReadOnlyList<string> existing,
        string interfaceName)
    {
        if (required.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                "No loopback aliases required by current config.", [], Fixable: false);

        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var missing = required.Where(ip => !have.Contains(ip)).ToList();

        if (missing.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                $"All {required.Count} required IP(s) present on {interfaceName}.",
                [], Fixable: false);

        var cmds = missing
            .Select(ip => $"netsh interface ip add address \"{interfaceName}\" {ip} 255.255.255.0")
            .ToList();

        return new CheckRow(Name, CheckStatus.Missing,
            $"Missing on {interfaceName}: {string.Join(", ", missing)}",
            cmds, Fixable: true);
    }
}
