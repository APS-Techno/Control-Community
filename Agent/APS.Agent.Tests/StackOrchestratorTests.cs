using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using APS.Agent.Cli.Commands;
using APS.Agent.Cli.Scoring;
using APS.Agent.Cli.Stack;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Skills;

namespace APS.Agent.Tests;

public sealed class StackOrchestratorTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aps-stack-test-{Guid.NewGuid():N}");

    public StackOrchestratorTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class FakeProcessRunner : IProcessRunner
    {
        public Dictionary<int, (string ImageName, DateTime CreationTimeUtc)> Live { get; } = [];
        public List<int> KilledPids { get; } = [];
        public int NextPid { get; set; } = 5000;

        public OwnedProcess StartProcess(
            string fileName,
            IReadOnlyList<string> args,
            string? workingDirectory = null,
            IDictionary<string, string?>? env = null)
        {
            int pid = NextPid++;
            var img = Path.GetFileName(fileName);
            var created = DateTime.UtcNow;
            Live[pid] = (img, created);
            return new OwnedProcess(pid, img, created);
        }

        public (string ImageName, DateTime CreationTimeUtc)? TryGetProcessIdentity(int pid) =>
            Live.TryGetValue(pid, out var info) ? info : null;

        public void KillProcess(int pid)
        {
            KilledPids.Add(pid);
            Live.Remove(pid);
        }

        public IReadOnlyList<int> FindProcessesByName(string imageName) =>
            Live.Where(kv => string.Equals(kv.Value.ImageName, imageName, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key)
                .ToList();
    }

    [Fact]
    public void StackPaths_ResolveFromEnvironment_ReadsEnvVarsAndRejectsMissing()
    {
        var env = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["ADS_BENCH_APP_DIR"] = Path.Combine(_dir, "app"),
            ["ADS_BENCH_CONTROL_BIN"] = Path.Combine(_dir, "bin", "APS.ControlCommunity.exe"),
            ["ADS_BENCH_EMULATOR_BIN"] = Path.Combine(_dir, "bin", "APS.EmulatorCommunity.exe"),
            ["ADS_BENCH_MCP_BIN"] = Path.Combine(_dir, "bin", "APS.Mcp.exe"),
            ["ADS_BENCH_CONFIGS_DIR"] = Path.Combine(_dir, "configs"),
            ["ADS_BENCH_SNAPSHOT_DIR"] = Path.Combine(_dir, "snapshots"),
            ["ADS_BENCH_SKILL_DIR"] = Path.Combine(_dir, "skill"),
            ["ADS_BENCH_SECRETS_DIR"] = Path.Combine(_dir, "secrets"),
        };

        var paths = StackPaths.ResolveFromEnvironment(k => env.TryGetValue(k, out var v) ? v : null);

        Assert.Equal(Path.GetFullPath(env["ADS_BENCH_CONTROL_BIN"]!), paths.ControlBin);
        Assert.Equal(Path.GetFullPath(env["ADS_BENCH_EMULATOR_BIN"]!), paths.EmulatorBin);
        Assert.Equal(Path.GetFullPath(env["ADS_BENCH_MCP_BIN"]!), paths.McpBin);
        Assert.Equal(Path.GetFullPath(env["ADS_BENCH_CONFIGS_DIR"]!), paths.ConfigsDir);
        Assert.Equal(Path.Combine(paths.SecretsDir, "apsmcp.ads.json"), paths.BridgeConfigPath("ads"));

        env.Remove("ADS_BENCH_CONFIGS_DIR");
        Assert.Throws<InvalidOperationException>(() =>
            StackPaths.ResolveFromEnvironment(k => env.TryGetValue(k, out var v) ? v : null));
    }

    [Theory]
    [InlineData("Sputter")]
    [InlineData("ADS")]
    [InlineData("Cluster")]
    public void SwitchRootConfig_UpdatesConfigDirAndPrefixForProfiles(string profile)
    {
        string configsRoot = Path.Combine(_dir, "Configs");
        string profileDir = Path.Combine(configsRoot, profile);
        Directory.CreateDirectory(profileDir);
        string rootXml = Path.Combine(_dir, "RootConfig.xml");
        File.WriteAllText(rootXml, """
            <Configuration>
              <ConfigDir>old</ConfigDir>
              <Prefix>OldProfile</Prefix>
              <WorkDir>Work</WorkDir>
              <HWDir>Hardware</HWDir>
              <GUIDir>GUI</GUIDir>
              <WorkerDir>Worker</WorkerDir>
              <RecipesDir>Recipes</RecipesDir>
              <LogsDir>Logs</LogsDir>
              <DataDir>Data</DataDir>
            </Configuration>
            """, Encoding.UTF8);

        var layout = StackOrchestrator.SwitchRootConfig(rootXml, profileDir, profile);

        var doc = XDocument.Load(rootXml);
        Assert.Equal(configsRoot, doc.Root!.Element("ConfigDir")!.Value);
        Assert.Equal(profile, doc.Root.Element("Prefix")!.Value);
        Assert.Equal(Path.Combine(profileDir, "Work"), layout.WorkDir);
        Assert.Equal(Path.Combine(profileDir, "Hardware"), layout.HwDir);
        Assert.Equal(Path.Combine(profileDir, "GUI"), layout.GuiDir);
        Assert.Equal(Path.Combine(profileDir, "Worker"), layout.WorkerDir);
        Assert.Equal(Path.Combine(profileDir, "Recipes"), layout.RecipesDir);
        Assert.Equal(Path.Combine(profileDir, "Logs"), layout.LogsDir);
        Assert.Equal(Path.Combine(profileDir, "Data"), layout.DataDir);
    }

    [Fact]
    public void StopOwnedStack_KillsOnlyMatchingImageAndCreationTime()
    {
        var tEmu = new DateTime(2026, 11, 16, 1, 0, 0, DateTimeKind.Utc);
        var tCtrl = new DateTime(2026, 11, 16, 1, 0, 1, DateTimeKind.Utc);
        var runner = new FakeProcessRunner();
        runner.Live[101] = ("APS.EmulatorCommunity.exe", tEmu);
        runner.Live[102] = ("APS.ControlCommunity.exe", tCtrl);
        runner.Live[103] = ("APS.ControlCommunity.exe", tCtrl.AddSeconds(5));
        runner.Live[104] = ("APS.EmulatorCommunity.exe", tEmu.AddSeconds(5));

        var handle = new OwnedStackHandle(
            "ADS",
            new OwnedProcess(101, "APS.EmulatorCommunity.exe", tEmu),
            new OwnedProcess(102, "APS.ControlCommunity.exe", tCtrl),
            new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));

        StackOrchestrator.StopOwnedStack(handle, runner);

        Assert.Equal([101, 102], runner.KilledPids.OrderBy(x => x).ToArray());
        Assert.True(runner.Live.ContainsKey(103));
        Assert.True(runner.Live.ContainsKey(104));
    }

    [Fact]
    public void RestoreRecipes_CopiesModifiedDeletesNewRestoresBaselineAndRejectsReparsePoint()
    {
        string recipesDir = Path.Combine(_dir, "Recipes");
        Directory.CreateDirectory(recipesDir);
        byte[] origA = Encoding.UTF8.GetBytes("<recipe name=\"BaseA\" />");
        byte[] origB = Encoding.UTF8.GetBytes("<recipe name=\"BaseB\" />");
        File.WriteAllBytes(Path.Combine(recipesDir, "BaseA.rcpx"), origA);
        File.WriteAllBytes(Path.Combine(recipesDir, "BaseB.rcpx"), origB);

        var baseline = StackOrchestrator.CaptureRecipeBaseline(recipesDir);

        File.WriteAllText(Path.Combine(recipesDir, "BaseA.rcpx"), "<recipe name=\"BaseA\" modified=\"1\" />", Encoding.UTF8);
        File.Delete(Path.Combine(recipesDir, "BaseB.rcpx"));
        File.WriteAllText(Path.Combine(recipesDir, "AgentNew.rcpx"), "<recipe name=\"AgentNew\" />", Encoding.UTF8);

        string artifactsDir = Path.Combine(_dir, "trial-artifacts");
        StackOrchestrator.RestoreRecipes(recipesDir, baseline, artifactsDir);

        Assert.True(File.Exists(Path.Combine(artifactsDir, "Recipes", "BaseA.rcpx")));
        Assert.True(File.Exists(Path.Combine(artifactsDir, "Recipes", "AgentNew.rcpx")));
        Assert.Equal(origA, File.ReadAllBytes(Path.Combine(recipesDir, "BaseA.rcpx")));
        Assert.Equal(origB, File.ReadAllBytes(Path.Combine(recipesDir, "BaseB.rcpx")));
        Assert.False(File.Exists(Path.Combine(recipesDir, "AgentNew.rcpx")));

        string junctionTarget = Path.Combine(_dir, "escaped-target");
        Directory.CreateDirectory(junctionTarget);
        string linkDir = Path.Combine(recipesDir, "LinkedDir");
        using var proc = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkDir}\" \"{junctionTarget}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        proc.WaitForExit();
        Assert.Equal(0, proc.ExitCode);

        Assert.Throws<InvalidOperationException>(() => StackOrchestrator.RestoreRecipes(recipesDir, baseline));
    }

    [Fact]
    public void BenchCommands_FreezeExport_StripsHostPathsAndPreservesValidChecksums()
    {
        string camp = Path.Combine(_dir, "campaign");
        string inputsDir = Path.Combine(camp, "inputs");
        string resultsDir = Path.Combine(camp, "results");
        string gradingDir = Path.Combine(camp, "grading");
        Directory.CreateDirectory(inputsDir);
        Directory.CreateDirectory(resultsDir);
        Directory.CreateDirectory(gradingDir);

        string suiteJson = "{\"suite\":\"ADS-Bench\",\"version\":\"2.0\",\"tasks\":[]}\n";
        string refJson = "{\"tasks\":[]}\n";
        string promptMd = "Operate safely.\n";
        File.WriteAllText(Path.Combine(inputsDir, "suite.json"), suiteJson, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(inputsDir, "scoring-reference.json"), refJson, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(inputsDir, "system_prompt.md"), promptMd, new UTF8Encoding(false));

        static string Sha(string p) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();

        string campaignJson = JsonSerializer.Serialize(new
        {
            campaign_id = "camp-test",
            track = "ads",
            input_sha256 = new Dictionary<string, string>
            {
                ["inputs/suite.json"] = Sha(Path.Combine(inputsDir, "suite.json")),
                ["inputs/scoring-reference.json"] = Sha(Path.Combine(inputsDir, "scoring-reference.json")),
                ["inputs/system_prompt.md"] = Sha(Path.Combine(inputsDir, "system_prompt.md")),
            },
            models = new[] { new { selector = "model-a", thinking = "none" } },
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(camp, "campaign.json"), campaignJson + "\n", new UTF8Encoding(false));

        string hostPathWin = string.Concat("X", ":\\work\\secret\\repo\\file.rcpx");
        string hostPathMac = "/Users/researcher/bench/file.tsv";
        string jRel = "results/t1.jsonl";
        string jPath = Path.Combine(camp, jRel);
        File.WriteAllText(
            jPath,
            "{\"type\":\"session_start\",\"ask\":\"test\"}\n" +
            $"{{\"type\":\"tool_result\",\"note\":{JsonSerializer.Serialize(hostPathWin + " " + hostPathMac)}}}\n",
            new UTF8Encoding(false));

        var sidecar = new AuditSidecar(
            "t1", "A01", "model-a", "none", "completed", $"Saved to {hostPathWin}", 1.5, 0.01, 1,
            "cfg", "schema", "skill", jRel, Sha(jPath), [new UsageRecord("model-a", 0.01)]);
        AuditJournalWriter.WriteSidecar(Path.Combine(resultsDir, "t1.json"), sidecar);
        File.WriteAllText(Path.Combine(camp, "camp-results.md"), $"Artifact in {hostPathMac}\n", new UTF8Encoding(false));

        string exportDir = Path.Combine(_dir, "export");
        BenchCommands.FreezeExport(camp, exportDir);

        string exportedJournal = File.ReadAllText(Path.Combine(exportDir, jRel), Encoding.UTF8);
        string exportedSidecar = File.ReadAllText(Path.Combine(exportDir, "results", "t1.json"), Encoding.UTF8);
        string exportedMd = File.ReadAllText(Path.Combine(exportDir, "camp-results.md"), Encoding.UTF8);

        Assert.DoesNotContain(hostPathWin, exportedJournal, StringComparison.Ordinal);
        Assert.DoesNotContain(hostPathMac, exportedJournal, StringComparison.Ordinal);
        Assert.DoesNotContain(hostPathWin, exportedSidecar, StringComparison.Ordinal);
        Assert.DoesNotContain(hostPathMac, exportedMd, StringComparison.Ordinal);

        string exportedJsonlSha = Sha(Path.Combine(exportDir, jRel));
        using var sidecarDoc = JsonDocument.Parse(exportedSidecar);
        Assert.Equal(exportedJsonlSha, sidecarDoc.RootElement.GetProperty("journal_sha256").GetString());
    }

    [Fact]
    public async Task Program_RunAsync_RoutesHelpUnknownAndBenchFreeze()
    {
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        int helpRc = await APS.Agent.Cli.Program.RunAsync(["--help"], new StringReader(""), outSw, errSw);
        Assert.Equal(0, helpRc);
        Assert.Contains("aps-agent", outSw.ToString());

        int badRc = await APS.Agent.Cli.Program.RunAsync(["bogus"], new StringReader(""), new StringWriter(), errSw);
        Assert.Equal(2, badRc);

        string suiteDir = Path.Combine(_dir, "suite");
        Directory.CreateDirectory(suiteDir);
        File.WriteAllText(Path.Combine(suiteDir, "ads-suite.json"), "{\"suite\":\"ADS-Bench\",\"version\":\"2.0\",\"tasks\":[]}\n");
        File.WriteAllText(Path.Combine(suiteDir, "scoring-reference.json"), "{\"tasks\":[]}\n");
        File.WriteAllText(Path.Combine(suiteDir, "system_prompt.md"), "Prompt\n");
        File.WriteAllText(Path.Combine(suiteDir, "AC1-brief.md"), "Brief\n");

        string camp = Path.Combine(_dir, "camp-cli");
        int freezeRc = await APS.Agent.Cli.Program.RunAsync(
            ["bench", "freeze", "--campaign", camp, "--track", "ads", "--models", "qwen3:medium,claude:none", "--suite-dir", suiteDir],
            new StringReader(""),
            new StringWriter(),
            errSw);
        Assert.Equal(0, freezeRc);
        Assert.True(File.Exists(Path.Combine(camp, "campaign.json")));
        Assert.True(File.Exists(Path.Combine(camp, "inputs", "AC1-brief.md")));
        string manifest = File.ReadAllText(Path.Combine(camp, "campaign.json"));
        Assert.Contains("inputs/suite.json", manifest);
        Assert.Contains("qwen3", manifest);
    }

    [Fact]
    public void CollectTrialArtifacts_CopiesSharedLogsAndWritesFinalStatusJson()
    {
        string profileDir = Path.Combine(_dir, "profile-ads");
        string logsDir = Path.Combine(profileDir, "Logs");
        Directory.CreateDirectory(logsDir);
        string liveLog = Path.Combine(logsDir, "Process_20261116.log");
        using (var held = new FileStream(liveLog, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
        {
            byte[] msg = Encoding.UTF8.GetBytes("2026-11-16 10:00:00.000 Recipe complete R\n");
            held.Write(msg);
            held.Flush();

            string artifactsDir = Path.Combine(_dir, "trial-artifacts-live");
            JsonElement sysStatus = JsonDocument.Parse("""{"isRecipeRunning":false,"activeAlarms":[],"chambers":[{"id":"Chamber 1","state":"Idle"}]}""").RootElement.Clone();
            StackOrchestrator.CollectTrialArtifacts(profileDir, artifactsDir, sysStatus);

            Assert.True(File.Exists(Path.Combine(artifactsDir, "Logs", "Process_20261116.log")));
            using var finalDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(artifactsDir, "final_status.json")));
            Assert.False(finalDoc.RootElement.GetProperty("system_status").GetProperty("isRecipeRunning").GetBoolean());
        }
    }

    [Fact]
    public void OperatorCommands_BuildSessionOptions_EnablesDirectDeviceOnlyInManualAutonomousTier()
    {
        var skill = new SkillBundle(_dir, "Rules", "sha", ["SKILL.md"]);
        var schema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        IReadOnlyList<ToolDeclaration> decls =
        [
            new("describe_machine", "Read", schema),
            new("upload_recipe", "Upload", schema),
            new("device_command", "Device", schema),
        ];

        var autoWithDevice = OperatorCommands.BuildSessionOptions(
            new OperatorCliOptions(AgentMode.Autonomous, ProviderProtocol.ChatCompletions, OperatorCommands.DefaultLocalEndpoint, "m", AllowDirectDeviceCommand: true),
            skill,
            null);
        Assert.Equal("Manual", autoWithDevice.SessionTier);
        Assert.Contains(
            ToolPolicy.FilterDeclarations(autoWithDevice.Mode, decls, autoWithDevice.SessionTier, autoWithDevice.AllowDirectDeviceCommand, null),
            t => t.Name == "device_command");

        var autoDefault = OperatorCommands.BuildSessionOptions(
            new OperatorCliOptions(AgentMode.Autonomous, ProviderProtocol.ChatCompletions, OperatorCommands.DefaultLocalEndpoint, "m", AllowDirectDeviceCommand: false),
            skill,
            null);
        Assert.DoesNotContain(
            ToolPolicy.FilterDeclarations(autoDefault.Mode, decls, autoDefault.SessionTier, autoDefault.AllowDirectDeviceCommand, null),
            t => t.Name == "device_command");

        var copilot = OperatorCommands.BuildSessionOptions(
            new OperatorCliOptions(AgentMode.Copilot, ProviderProtocol.ChatCompletions, OperatorCommands.DefaultLocalEndpoint, "m", AllowDirectDeviceCommand: true),
            skill,
            null);
        Assert.Equal("Read", copilot.SessionTier);
        Assert.Single(ToolPolicy.FilterDeclarations(copilot.Mode, decls, copilot.SessionTier, copilot.AllowDirectDeviceCommand, null));
    }

    [Fact]
    public void WindowsProcessRunner_TryGetProcessIdentity_ReturnsCurrentProcessIdentity()
    {
        var runner = new WindowsProcessRunner();
        var identity = runner.TryGetProcessIdentity(Environment.ProcessId);
        Assert.NotNull(identity);
        Assert.False(string.IsNullOrWhiteSpace(identity.Value.ImageName));
    }

    [Fact]
    public void BenchCommands_Freeze_EmbedsAttachmentsWithSha256Verification()
    {
        string root = Path.Combine(_dir, "bench-src");
        string suiteDir = Path.Combine(root, "suite");
        string fixturesDir = Path.Combine(root, "fixtures");
        Directory.CreateDirectory(suiteDir);
        Directory.CreateDirectory(fixturesDir);

        byte[] logBytes = Encoding.UTF8.GetBytes("1027  16:58:53.390  [Execute]  exec: pwait TC1 0 120\n");
        string logPath = Path.Combine(fixturesDir, "A09-log.txt");
        File.WriteAllBytes(logPath, logBytes);
        string logSha = Convert.ToHexString(SHA256.HashData(logBytes)).ToLowerInvariant();

        File.WriteAllText(
            Path.Combine(fixturesDir, "fixtures.json"),
            JsonSerializer.Serialize(new
            {
                attachments = new Dictionary<string, object>
                {
                    ["A09-log"] = new { file = "A09-log.txt", sha256 = logSha },
                },
            }));
        File.WriteAllText(
            Path.Combine(suiteDir, "ads-suite.json"),
            """{"suite":"ads","version":"2.0","tasks":[{"id":"A09","type":"short","key":"read","ask":"Check log","attachments":["A09-log"],"tools":["describe_machine"]}]}""");
        File.WriteAllText(Path.Combine(suiteDir, "scoring-reference.json"), """{"tasks":[]}""");
        File.WriteAllText(Path.Combine(suiteDir, "system_prompt.md"), "Prompt\n");
        File.WriteAllText(Path.Combine(suiteDir, "AC1-brief.md"), "Brief\n");

        string camp = Path.Combine(_dir, "camp-attach");
        BenchCommands.Freeze(camp, "ads", [("model-a", "none")], suiteDir);

        using var frozenDoc = JsonDocument.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "suite.json")));
        string embeddedAsk = frozenDoc.RootElement.GetProperty("tasks")[0].GetProperty("ask").GetString()!;
        Assert.Contains("Attached file: A09-log.txt", embeddedAsk);
        Assert.Contains("exec: pwait TC1 0 120", embeddedAsk);

        File.WriteAllText(logPath, "tampered\n");
        string campBad = Path.Combine(_dir, "camp-attach-bad");
        Assert.Throws<InvalidOperationException>(() =>
            BenchCommands.Freeze(campBad, "ads", [("model-a", "none")], suiteDir));
    }

    [Fact]
    public void BenchCommands_ResolvesTaskKeyTierAndAllowedTools()
    {
        using var readDoc = JsonDocument.Parse("""{"id":"A01","key":"read","tools":["describe_machine","recipe_status"]}""");
        Assert.Equal(("read", "Read"), BenchCommands.ResolveTaskKeyAndTier(readDoc.RootElement));
        Assert.Equal(
            ["describe_machine", "recipe_status"],
            BenchCommands.ResolveTaskAllowedTools(readDoc.RootElement)!.OrderBy(x => x).ToArray());

        using var recipeDoc = JsonDocument.Parse("""{"id":"A10","key":"recipe","tools":["describe_machine","validate_recipe"]}""");
        Assert.Equal(("recipe", "Recipe"), BenchCommands.ResolveTaskKeyAndTier(recipeDoc.RootElement));

        using var manualDoc = JsonDocument.Parse("""{"id":"M01","key":"manual"}""");
        Assert.Equal(("manual", "Manual"), BenchCommands.ResolveTaskKeyAndTier(manualDoc.RootElement));

        using var legacyDoc = JsonDocument.Parse("""{"id":"X01","allowed_tools":["system_status"]}""");
        Assert.Equal(["system_status"], BenchCommands.ResolveTaskAllowedTools(legacyDoc.RootElement)!.ToArray());
    }

    [Fact]
    public void CollectTrialArtifacts_CopiesLogsFromAdditionalLogsDir()
    {
        string profileDir = Path.Combine(_dir, "profile-empty");
        string appLogsDir = Path.Combine(_dir, "app-bin", "Logs");
        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(appLogsDir);
        File.WriteAllText(Path.Combine(appLogsDir, "Process_20261116.log"), "2026-11-16 10:00:00.000 Recipe complete R\n");

        string dest = Path.Combine(_dir, "trial-add-logs");
        StackOrchestrator.CollectTrialArtifacts(profileDir, dest, null, additionalLogsDir: appLogsDir);
        Assert.True(File.Exists(Path.Combine(dest, "Logs", "Process_20261116.log")));
    }
}
