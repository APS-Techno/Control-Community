using EmulatorCore.Models;

namespace APS.SystemEmulator.Setup;

public record ConfigPrerequisites(
    IReadOnlyList<string> LoopbackIps,
    IReadOnlyList<int> FirewallPorts,
    IReadOnlyList<string> ComPorts)
{
    private static readonly HashSet<string> LocalBinds =
        new(StringComparer.OrdinalIgnoreCase) { "127.0.0.1", "0.0.0.0", "localhost", "::1" };

    public static ConfigPrerequisites From(EmulatorConfig cfg)
    {
        var ips = cfg.Protocols
            .Where(p => p is S7ServerConfig || p is TcpSerialConfig)
            .Select(p => p.BindAddress)
            .Where(a => !string.IsNullOrWhiteSpace(a) && !LocalBinds.Contains(a))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var ports = cfg.Protocols
            .Where(p => p is S7ServerConfig || p is TcpSerialConfig)
            .Select(p => p.Port)
            .Where(n => n > 0)
            .Distinct()
            .ToList();

        var coms = cfg.Protocols
            .OfType<SerialConfig>()
            .Select(s => s.PortName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new ConfigPrerequisites(ips, ports, coms);
    }
}
