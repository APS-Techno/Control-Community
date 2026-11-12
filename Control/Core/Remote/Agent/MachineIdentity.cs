using System.Net;
using System.Xml.Linq;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Whether the loaded profile drives a simulator or physical hardware. Fails safe toward
/// "physical": any non-loopback endpoint, any serial port, or no endpoint at all means physical.</summary>
internal static class MachineIdentity
{
    internal const string Emulator = "emulator";
    internal const string Physical = "physical";

    internal static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        host = host.Trim();
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
    }

    internal static bool DecideIsEmulator(
        IReadOnlyCollection<string> plcAddresses, IReadOnlyCollection<string> lanHosts,
        bool hasSerialPort, bool? overrideValue)
    {
        if (overrideValue is { } forced) return forced;
        if (hasSerialPort) return false;
        if (plcAddresses.Count + lanHosts.Count == 0) return false;
        return plcAddresses.All(IsLoopbackHost) && lanHosts.All(IsLoopbackHost);
    }

    internal static bool IsEmulatorProfile(XDocument? hwConf, bool? overrideValue)
    {
        if (hwConf?.Root is not { } root) return overrideValue ?? false;

        var plcs = root.Descendants("plc").ToList();
        var plcIds = plcs.Select(p => p.Value.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var plcAddresses = plcs.Select(p => (string?)p.Attribute("address") ?? "").ToList();

        var hosts = new List<string>();
        bool serial = false;
        foreach (var el in root.Descendants())
        {
            if (el.Name.LocalName == "plc") continue;

            string? host = (string?)el.Attribute("host");
            if (host is not null && !plcIds.Contains(host.Trim()))
                hosts.Add(host);

            string? port = (string?)el.Attribute("port");
            if (port is null) continue;
            if (port.StartsWith("tcp://", StringComparison.OrdinalIgnoreCase))
                hosts.Add(TcpUriHost(port));
            else if (!int.TryParse(port, System.Globalization.NumberStyles.Integer,
                         System.Globalization.CultureInfo.InvariantCulture, out _))
                serial = true;
        }
        return DecideIsEmulator(plcAddresses, hosts, serial, overrideValue);
    }

    internal static bool? ParseOverride(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        Emulator => true,
        Physical => false,
        _ => null,
    };

    internal static bool? LoadOverride(string? configDir)
    {
        if (string.IsNullOrEmpty(configDir)) return null;
        string path = Path.Combine(configDir, "Config.xml");
        try
        {
            if (!File.Exists(path)) return null;
            return ParseOverride(XDocument.Load(path).Root?.Element("ExecutionTarget")?.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            System.Diagnostics.Trace.TraceWarning($"MachineIdentity: {path}: {ex.Message}");
            return null;
        }
    }

    internal static (bool IsEmulator, string ExecutionTarget) Resolve(string? configDir, string? hwConfPath)
    {
        XDocument? hw = null;
        try
        {
            if (!string.IsNullOrEmpty(hwConfPath) && File.Exists(hwConfPath))
                hw = XDocument.Load(hwConfPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            System.Diagnostics.Trace.TraceWarning($"MachineIdentity: {hwConfPath}: {ex.Message}");
        }
        bool isEmulator = IsEmulatorProfile(hw, LoadOverride(configDir));
        return (isEmulator, isEmulator ? Emulator : Physical);
    }

    private static string TcpUriHost(string uri)
    {
        string rest = uri["tcp://".Length..];
        int colon = rest.LastIndexOf(':');
        return colon < 0 ? rest : rest[..colon];
    }
}
