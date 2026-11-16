using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Skills;

namespace APS.Agent.Cli.Stack;

public sealed record TrackSpec(
    string Key,
    string Profile,
    string SuiteFile,
    string BriefFile,
    string DefaultSnapshot)
{
    public static readonly TrackSpec Sputter = new("sputter", "Sputter", "sputter-suite.json", "SC1-brief.md", "Sputter_Idle.emstate");
    public static readonly TrackSpec Ads = new("ads", "ADS", "ads-suite.json", "AC1-brief.md", "ADS_Idle.emstate");
    public static readonly TrackSpec Cluster = new("cluster", "Cluster", "cluster-suite.json", "CC1-brief.md", "Cluster_Idle.emstate");

    public static IReadOnlyList<TrackSpec> Resolve(string trackKey) =>
        (trackKey ?? "").Trim().ToLowerInvariant() switch
        {
            "sputter" => [Sputter],
            "ads" => [Ads],
            "cluster" => [Cluster],
            "all" => [Sputter, Ads, Cluster],
            _ => throw new ArgumentException($"Unknown track '{trackKey}'. Expected sputter, ads, cluster, or all.", nameof(trackKey)),
        };
}

public sealed record StackPaths(
    string AppDir,
    string ControlBin,
    string EmulatorBin,
    string McpBin,
    string ConfigsDir,
    string SnapshotDir,
    string SkillDir,
    string SecretsDir)
{
    public static StackPaths ResolveFromEnvironment(Func<string, string?>? getEnv = null)
    {
        Func<string, string?> reader = getEnv ?? Environment.GetEnvironmentVariable;
        string? rawAppDir = reader("ADS_BENCH_APP_DIR");
        string? rawCtrl = reader("ADS_BENCH_CONTROL_BIN");
        string? rawEmu = reader("ADS_BENCH_EMULATOR_BIN");
        string? rawMcp = reader("ADS_BENCH_MCP_BIN");

        string controlBin = ResolveBinary(rawCtrl, rawAppDir, StackOrchestrator.ControlImageName, "ADS_BENCH_CONTROL_BIN");
        string emulatorBin = ResolveBinary(rawEmu, rawAppDir, StackOrchestrator.EmulatorImageName, "ADS_BENCH_EMULATOR_BIN");
        string mcpBin = ResolveBinary(rawMcp, rawAppDir, StackOrchestrator.McpImageName, "ADS_BENCH_MCP_BIN");

        string appDir = !string.IsNullOrWhiteSpace(rawAppDir)
            ? Path.GetFullPath(rawAppDir)
            : Path.GetDirectoryName(controlBin) ?? throw new InvalidOperationException("ADS_BENCH_APP_DIR could not be resolved.");

        string configsDir = RequireDir(reader, "ADS_BENCH_CONFIGS_DIR");
        string snapshotDir = RequireDir(reader, "ADS_BENCH_SNAPSHOT_DIR");
        string skillDir = RequireDir(reader, "ADS_BENCH_SKILL_DIR");
        string secretsDir = string.IsNullOrWhiteSpace(reader("ADS_BENCH_SECRETS_DIR"))
            ? ""
            : Path.GetFullPath(reader("ADS_BENCH_SECRETS_DIR")!);

        return new StackPaths(appDir, controlBin, emulatorBin, mcpBin, configsDir, snapshotDir, skillDir, secretsDir);
    }

    public string BridgeConfigPath(string key)
    {
        if (string.IsNullOrWhiteSpace(SecretsDir))
            throw new InvalidOperationException("ADS_BENCH_SECRETS_DIR is not configured.");
        return Path.Combine(SecretsDir, $"apsmcp.{key}.json");
    }

    public string RootConfigPath() => Path.Combine(AppDir, "RootConfig.xml");

    public string ProfileDir(string profile) => Path.Combine(ConfigsDir, profile);

    public string SnapshotPath(string snapshotFileName) => Path.Combine(SnapshotDir, snapshotFileName);

    private static string ResolveBinary(string? explicitPath, string? appDir, string defaultFileName, string envName)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return Path.GetFullPath(explicitPath);
        if (!string.IsNullOrWhiteSpace(appDir))
            return Path.GetFullPath(Path.Combine(appDir, defaultFileName));
        throw new InvalidOperationException($"{envName} (or ADS_BENCH_APP_DIR) must be set.");
    }

    private static string RequireDir(Func<string, string?> reader, string envName)
    {
        string? val = reader(envName);
        if (string.IsNullOrWhiteSpace(val))
            throw new InvalidOperationException($"Required environment variable '{envName}' is not set.");
        return Path.GetFullPath(val);
    }
}

public sealed record RootConfigLayout(
    string ConfigDir,
    string Prefix,
    string WorkDir,
    string HwDir,
    string GuiDir,
    string WorkerDir,
    string RecipesDir,
    string LogsDir,
    string DataDir);

public sealed record OwnedProcess(int Pid, string ImageName, DateTime CreationTimeUtc);

public sealed record OwnedStackHandle(
    string Profile,
    OwnedProcess Emulator,
    OwnedProcess Control,
    IReadOnlyDictionary<string, byte[]> BaselineRecipes);

public interface IProcessRunner
{
    OwnedProcess StartProcess(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory = null,
        IDictionary<string, string?>? env = null);

    (string ImageName, DateTime CreationTimeUtc)? TryGetProcessIdentity(int pid);

    void KillProcess(int pid);

    IReadOnlyList<int> FindProcessesByName(string imageName);
}

public sealed class WindowsProcessRunner : IProcessRunner
{
    public OwnedProcess StartProcess(
        string fileName,
        IReadOnlyList<string> args,
        string? workingDirectory = null,
        IDictionary<string, string?>? env = null)
    {
        var psi = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(fileName) ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (string a in args)
            psi.ArgumentList.Add(a);
        if (env is not null)
        {
            foreach (var kv in env)
            {
                if (kv.Value is null) psi.Environment.Remove(kv.Key);
                else psi.Environment[kv.Key] = kv.Value;
            }
        }

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");
        DateTime createdUtc;
        try { createdUtc = proc.StartTime.ToUniversalTime(); }
        catch { createdUtc = DateTime.UtcNow; }
        return new OwnedProcess(proc.Id, Path.GetFileName(fileName), createdUtc);
    }

    public (string ImageName, DateTime CreationTimeUtc)? TryGetProcessIdentity(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (proc.HasExited) return null;
            string name;
            try
            {
                name = Path.GetFileName(proc.MainModule?.FileName ?? $"{proc.ProcessName}.exe");
            }
            catch
            {
                name = $"{proc.ProcessName}.exe";
            }
            return (name, proc.StartTime.ToUniversalTime());
        }
        catch
        {
            return null;
        }
    }

    public void KillProcess(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                proc.WaitForExit(5000);
            }
        }
        catch (ArgumentException) { }
    }

    public IReadOnlyList<int> FindProcessesByName(string imageName)
    {
        string stem = Path.GetFileNameWithoutExtension(imageName);
        var pids = new List<int>();
        foreach (var p in Process.GetProcessesByName(stem))
        {
            using (p)
                pids.Add(p.Id);
        }
        return pids;
    }
}

public static class StackOrchestrator
{
    public const string ControlImageName = "APS.ControlCommunity.exe";
    public const string EmulatorImageName = "APS.EmulatorCommunity.exe";
    public const string McpImageName = "APS.Mcp.exe";

    public static RootConfigLayout SwitchRootConfig(string rootConfigXmlPath, string profileDir, string profile)
    {
        if (string.IsNullOrWhiteSpace(profile))
            throw new ArgumentException("Profile name is required.", nameof(profile));
        string fullProfileDir = Path.GetFullPath(profileDir);
        string configDir = Path.GetDirectoryName(fullProfileDir)
            ?? throw new InvalidOperationException($"Cannot determine parent ConfigDir for '{profileDir}'.");

        var doc = XDocument.Load(rootConfigXmlPath);
        XElement root = doc.Root ?? throw new InvalidOperationException($"Invalid RootConfig XML: {rootConfigXmlPath}");
        SetElementValue(root, "ConfigDir", configDir);
        SetElementValue(root, "Prefix", profile);
        doc.Save(rootConfigXmlPath);

        return new RootConfigLayout(
            configDir,
            profile,
            ResolveSubdir(fullProfileDir, root, "WorkDir", "Work"),
            ResolveSubdir(fullProfileDir, root, "HWDir", "Hardware"),
            ResolveSubdir(fullProfileDir, root, "GUIDir", "GUI"),
            ResolveSubdir(fullProfileDir, root, "WorkerDir", "Worker"),
            ResolveSubdir(fullProfileDir, root, "RecipesDir", "Recipes"),
            ResolveSubdir(fullProfileDir, root, "LogsDir", "Logs"),
            ResolveSubdir(fullProfileDir, root, "DataDir", "Data"));
    }

    public static IReadOnlyDictionary<string, byte[]> CaptureRecipeBaseline(string recipesDir)
    {
        string root = Path.GetFullPath(recipesDir);
        var result = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;

        EnsureNoReparsePoints(root);
        var opts = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = true };
        foreach (string file in Directory.EnumerateFiles(root, "*.rcpx", opts).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            result[rel] = File.ReadAllBytes(file);
        }
        return result;
    }

    public static void RestoreRecipes(
        string recipesDir,
        IReadOnlyDictionary<string, byte[]> baseline,
        string? trialArtifactsDir = null)
    {
        string root = Path.GetFullPath(recipesDir);
        Directory.CreateDirectory(root);
        EnsureNoReparsePoints(root);

        var opts = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = true };
        foreach (string file in Directory.EnumerateFiles(root, "*", opts).ToList())
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            byte[] current = File.ReadAllBytes(file);
            bool inBaseline = baseline.TryGetValue(rel, out var baseBytes);
            bool modified = inBaseline && !current.AsSpan().SequenceEqual(baseBytes);
            bool added = !inBaseline;

            if ((added || modified) && !string.IsNullOrWhiteSpace(trialArtifactsDir)
                && string.Equals(Path.GetExtension(file), ".rcpx", StringComparison.OrdinalIgnoreCase))
            {
                string dest = ConfineUnder(Path.Combine(Path.GetFullPath(trialArtifactsDir), "Recipes"), rel);
                string? parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                File.WriteAllBytes(dest, current);
            }

            if (added)
                File.Delete(file);
        }

        foreach (var (rel, origBytes) in baseline)
        {
            string target = ConfineUnder(root, rel);
            string? parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            if (!File.Exists(target) || !File.ReadAllBytes(target).AsSpan().SequenceEqual(origBytes))
                File.WriteAllBytes(target, origBytes);
        }
    }

    public static void CollectTrialArtifacts(
        string profileDir,
        string trialArtifactsDir,
        JsonElement? finalSystemStatus = null,
        string? additionalLogsDir = null)
    {
        string prof = Path.GetFullPath(profileDir);
        string dest = Path.GetFullPath(trialArtifactsDir);
        Directory.CreateDirectory(dest);

        string dstLogs = Path.Combine(dest, "Logs");
        foreach (string srcLogs in new[] { Path.Combine(prof, "Logs"), additionalLogsDir }
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => Path.GetFullPath(d!))
            .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(srcLogs)) continue;
            Directory.CreateDirectory(dstLogs);
            foreach (string logFile in Directory.EnumerateFiles(srcLogs, "Process_*.log", SearchOption.TopDirectoryOnly))
                CopyShared(logFile, Path.Combine(dstLogs, Path.GetFileName(logFile)));
        }

        if (finalSystemStatus is { } status)
        {
            string json = JsonSerializer.Serialize(new { system_status = status }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dest, "final_status.json"), json + "\n", new UTF8Encoding(false));
        }
    }

    private static void CopyShared(string src, string dst)
    {
        using var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);
    }

    public static Dictionary<string, string> ComputeProfileHashes(string profileDir)
    {
        string prof = Path.GetFullPath(profileDir);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] subdirs = ["Hardware", "GUI", "Work", "Worker", "Recipes"];
        foreach (string sub in subdirs)
        {
            string dir = Path.Combine(prof, sub);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            {
                string rel = Path.GetRelativePath(prof, file).Replace('\\', '/');
                hashes[rel] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant();
            }
        }
        return hashes;
    }

    public static OwnedStackHandle StartStack(
        StackPaths paths,
        TrackSpec track,
        IProcessRunner? runner = null,
        string? snapshotFileName = null,
        int? emulatorHeadStartMs = null)
    {
        IProcessRunner procRunner = runner ?? new WindowsProcessRunner();
        EnsureNoStrayCommunityProcesses(procRunner);

        string profileDir = paths.ProfileDir(track.Profile);
        RootConfigLayout layout = SwitchRootConfig(paths.RootConfigPath(), profileDir, track.Profile);
        var baseline = CaptureRecipeBaseline(layout.RecipesDir);

        string snapshotPath = paths.SnapshotPath(snapshotFileName ?? track.DefaultSnapshot);
        OwnedProcess emu = procRunner.StartProcess(
            paths.EmulatorBin,
            ["--project", track.Profile, "--state", snapshotPath],
            Path.GetDirectoryName(paths.EmulatorBin));

        int headStart = emulatorHeadStartMs ?? (runner is null ? 3000 : 0);
        if (headStart > 0)
            Thread.Sleep(headStart);

        var ctrlEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["APS_DEV_CONTROL_MODE"] = "Remote",
        };
        try
        {
            OwnedProcess ctrl = procRunner.StartProcess(
                paths.ControlBin,
                [],
                Path.GetDirectoryName(paths.ControlBin),
                ctrlEnv);
            return new OwnedStackHandle(track.Profile, emu, ctrl, baseline);
        }
        catch
        {
            KillIfOwned(emu, procRunner);
            throw;
        }
    }

    public static void StopOwnedStack(OwnedStackHandle handle, IProcessRunner? runner = null)
    {
        IProcessRunner procRunner = runner ?? new WindowsProcessRunner();
        KillIfOwned(handle.Control, procRunner);
        KillIfOwned(handle.Emulator, procRunner);
    }

    public static async Task WaitReadyAsync(
        IMcpToolInvoker mcp,
        TimeSpan timeout,
        CancellationToken ct = default)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        var emptyArgs = JsonDocument.Parse("{}").RootElement.Clone();

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                ToolCallReceipt machine = await mcp.CallAsync("describe_machine", emptyArgs, ct).ConfigureAwait(false);
                ToolCallReceipt status = await mcp.CallAsync("recipe_status", emptyArgs, ct).ConfigureAwait(false);
                if (!machine.IsError && !status.IsError)
                    return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
            await Task.Delay(500, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"APS stack did not become ready within {timeout.TotalSeconds:0}s.", lastError);
    }

    private static void KillIfOwned(OwnedProcess owned, IProcessRunner runner)
    {
        var live = runner.TryGetProcessIdentity(owned.Pid);
        if (live is null) return;
        if (!string.Equals(live.Value.ImageName, owned.ImageName, StringComparison.OrdinalIgnoreCase)) return;
        if (Math.Abs((live.Value.CreationTimeUtc - owned.CreationTimeUtc).TotalMilliseconds) > 100.0) return;
        runner.KillProcess(owned.Pid);
    }

    private static void EnsureNoStrayCommunityProcesses(IProcessRunner runner)
    {
        int[] strayCtrl = [.. runner.FindProcessesByName(ControlImageName)];
        int[] strayEmu = [.. runner.FindProcessesByName(EmulatorImageName)];
        if (strayCtrl.Length > 0 || strayEmu.Length > 0)
        {
            throw new InvalidOperationException(
                $"Stray community processes detected (Control: [{string.Join(",", strayCtrl)}], Emulator: [{string.Join(",", strayEmu)}]). Stop them before launching benchmark stack.");
        }
    }

    private static void EnsureNoReparsePoints(string rootDir)
    {
        var rootInfo = new DirectoryInfo(rootDir);
        if ((rootInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Reparse point / symlink not permitted at '{rootDir}'.");

        var opts = new EnumerationOptions { AttributesToSkip = 0, RecurseSubdirectories = true };
        foreach (string dir in Directory.EnumerateDirectories(rootDir, "*", opts))
        {
            if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Reparse point / symlink not permitted in recipes: '{dir}'.");
        }
        foreach (string file in Directory.EnumerateFiles(rootDir, "*", opts))
        {
            if ((new FileInfo(file).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Reparse point / symlink not permitted in recipes: '{file}'.");
        }
    }

    private static string ConfineUnder(string rootDir, string relativePath)
    {
        string rootFull = Path.GetFullPath(rootDir);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
            rootFull += Path.DirectorySeparatorChar;
        string combined = Path.GetFullPath(Path.Combine(rootFull, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path escapes root '{rootDir}': '{relativePath}'.");
        return combined;
    }

    private static void SetElementValue(XElement parent, string name, string value)
    {
        XElement? el = parent.Element(name);
        if (el is null)
            parent.Add(new XElement(name, value));
        else
            el.Value = value;
    }

    private static string ResolveSubdir(string profileDir, XElement root, string elementName, string defaultName)
    {
        string rel = root.Element(elementName)?.Value.Trim() ?? defaultName;
        return Path.GetFullPath(Path.IsPathRooted(rel) ? rel : Path.Combine(profileDir, rel));
    }
}
