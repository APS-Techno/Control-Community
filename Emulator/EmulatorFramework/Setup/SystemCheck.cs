using System.Diagnostics;
using EmulatorCore.Models;

namespace APS.SystemEmulator.Setup;

public static class SystemCheck
{
    public record Summary(IReadOnlyList<CheckRow> Rows)
    {
        public bool AllOk => Rows.All(r => r.Status == CheckStatus.Ok);
        public IReadOnlyList<CheckRow> Fixable =>
            Rows.Where(r => r.Status == CheckStatus.Missing && r.Fixable).ToList();
    }

    public static Summary RunAll(EmulatorConfig cfg)
    {
        var prereq = ConfigPrerequisites.From(cfg);

        var rows = new List<CheckRow>
        {
            RuntimeCheck.Run(),
            LoopbackAdapterSetup.Run(),
            VirtualSerialSetup.BuildCheckRow(cfg.Protocols.OfType<SerialConfig>().ToList()),
            LoopbackAliasSetup.Run(prereq.LoopbackIps),
            FirewallRuleSetup.Run(prereq.FirewallPorts),
        };

        return new Summary(rows);
    }

    /// <summary>Next to the emulator binary so the user can inspect it after a failed Fix run.</summary>
    private static string LogPath => Path.Combine(AppContext.BaseDirectory, "prepare-system.log");

    /// <summary>Runs cmd.exe directly instead of dropping a wrapper .cmd: Bitdefender's ATD
    /// quarantines freshly-written elevated .cmd files regardless of path exceptions ("dropper"
    /// heuristic).</summary>
    public static string RunFixBatch(IEnumerable<string> commands)
    {
        var cmds = commands.ToList();
        if (cmds.Count == 0) return "(nothing to do)";

        string logPath = LogPath;

        // `&` chains unconditionally; `^&` inside the echo lines prints a literal `&`.
        var parts = new List<string>
        {
            "echo === APS Prepare System fix batch ===",
            "echo Timestamp: %DATE% %TIME%",
        };
        foreach (var c in cmds)
        {
            parts.Add("echo.");
            parts.Add($"echo ^> {c.Replace("^", "^^").Replace("&", "^&")}");
            parts.Add(c);
        }

        string inner = string.Join(" & ", parts);
        string args = $"/c \"({inner}) > \"\"{logPath}\"\" 2>&1\"";

        try
        {
            // Pre-create the log so a sharing-violation read still finds the file.
            File.WriteAllText(logPath, string.Empty);

            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            })!;
            p.WaitForExit(120_000);

            return ReadSharedWithRetry(logPath);
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>Open with FileShare.ReadWrite + retry, so we don't lose the read race
    /// with Bitdefender's on-access scanner holding the file open just after write.</summary>
    private static string ReadSharedWithRetry(string path)
    {
        const int attempts = 6;
        IOException? last = null;
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                return sr.ReadToEnd();
            }
            catch (IOException ex)
            {
                last = ex;
                Thread.Sleep(200);
            }
        }
        return $"Error reading log: {last?.Message}{Environment.NewLine}Log preserved at {path}";
    }
}
