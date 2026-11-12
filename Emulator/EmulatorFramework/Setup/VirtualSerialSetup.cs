using System.IO.Ports;
using EmulatorCore.Models;

namespace APS.SystemEmulator.Setup;

/// <summary>
/// The in-house APS VirtualSerial driver replaces FVSP/com0com: it pairs the configured
/// emulator-side COM ports (e.g. COM104..COM121) with the HW-side ports the control software
/// uses. Detection opens each configured port -- the strongest signal the driver is actually
/// serving the pair.
/// </summary>
public static class VirtualSerialSetup
{
    private const string Name = "APS VirtualSerial driver";

    private static string? ResolveInstallScript()
    {
        string[] candidates =
        [
            @"C:\Program Files\APS\VirtualSerial\install.cmd",
            Path.Combine(AppContext.BaseDirectory, "VirtualSerial", "install.cmd"),
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private static bool CanOpen(string portName)
    {
        try
        {
            using var sp = new SerialPort(portName, 9600);
            sp.Open();
            sp.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static CheckRow BuildCheckRow(IReadOnlyList<SerialConfig> serialConfigs)
    {
        if (serialConfigs.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                "No serial devices configured.", [], Fixable: false);

        var unopenable = serialConfigs
            .Select(c => c.PortName)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct()
            .Where(p => !CanOpen(p))
            .ToList();

        if (unopenable.Count == 0)
            return new CheckRow(Name, CheckStatus.Ok,
                $"All {serialConfigs.Count} port(s) served by APS VirtualSerial.",
                [], Fixable: false);

        string ports = string.Join(", ", unopenable);
        string? installScript = ResolveInstallScript();

        if (installScript == null)
            return new CheckRow(Name, CheckStatus.Missing,
                $"Cannot open: {ports}. Install the APS VirtualSerial driver "
                + "(run the VirtualSerial kit's install.cmd as administrator).",
                [], Fixable: false);

        return new CheckRow(Name, CheckStatus.Missing,
            $"Cannot open: {ports}. APS VirtualSerial driver missing or not serving these ports.",
            [$"\"{installScript}\""], Fixable: true);
    }
}
