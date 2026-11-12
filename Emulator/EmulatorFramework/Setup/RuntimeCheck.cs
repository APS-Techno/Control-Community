using System.Diagnostics;
using System.Text.RegularExpressions;

namespace APS.SystemEmulator.Setup;

public static partial class RuntimeCheck
{
    private const string Name = ".NET 9 Desktop Runtime";

    public static CheckRow Run()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--list-runtimes")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return ParseAndEvaluate(output);
        }
        catch (Exception ex)
        {
            return new CheckRow(Name, CheckStatus.Unknown,
                $"Could not run 'dotnet --list-runtimes': {ex.Message}",
                [], Fixable: false);
        }
    }

    public static CheckRow ParseAndEvaluate(string listRuntimesOutput)
    {
        foreach (Match m in DesktopRegex().Matches(listRuntimesOutput))
        {
            if (int.TryParse(m.Groups[1].Value, out int major) && major >= 9)
                return new CheckRow(Name, CheckStatus.Ok,
                    $"Found Microsoft.WindowsDesktop.App {m.Groups[0].Value.Split(' ')[1]}",
                    [], Fixable: false);
        }
        return new CheckRow(Name, CheckStatus.Missing,
            "Microsoft.WindowsDesktop.App 9.x not found. Install the .NET 9 Desktop Runtime from dotnet.microsoft.com/download/dotnet/9.0.",
            [], Fixable: false);
    }

    [GeneratedRegex(@"Microsoft\.WindowsDesktop\.App\s+(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex DesktopRegex();
}
