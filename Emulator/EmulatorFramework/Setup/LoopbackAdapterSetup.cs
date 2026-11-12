using System.Diagnostics;
using System.Net.NetworkInformation;

namespace APS.SystemEmulator.Setup;

public static class LoopbackAdapterSetup
{
    private const string Name = "Loopback interface";

    public static CheckRow Run()
    {
        try
        {
            return Evaluate(ResolveLoopbackName());
        }
        catch (Exception ex)
        {
            return new CheckRow(Name, CheckStatus.Unknown,
                $"Could not enumerate network interfaces: {ex.Message}", [], Fixable: false);
        }
    }

    public static CheckRow Evaluate(string? loopbackName)
    {
        if (!string.IsNullOrEmpty(loopbackName))
            return new CheckRow(Name, CheckStatus.Ok,
                $"Present: \"{loopbackName}\"", [], Fixable: false);

        return new CheckRow(Name, CheckStatus.Missing,
            "No loopback interface found. Add a Microsoft KM-TEST Loopback Adapter via Device Manager " +
            "(see Help > Setup > Network Configuration).",
            [], Fixable: false);
    }

    /// <summary>Matches by <see cref="NetworkInterfaceType.Loopback"/>, not by the "Loopback" name: that
    /// name is translated on localized Windows (e.g. Russian). Returns the OS friendly name so callers
    /// can feed it straight back to netsh for alias commands.</summary>
    public static string? ResolveLoopbackName() => FindLoopback()?.Name;

    public static NetworkInterface? FindLoopback()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Loopback);
    }

    internal static string RunNetsh(string args)
    {
        var psi = new ProcessStartInfo("netsh", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return output;
    }
}
