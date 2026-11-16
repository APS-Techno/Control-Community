using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using APS.Agent.Cli.Scoring;
using APS.Agent.Cli.Stack;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Serialization;
using APS.Agent.Core.Skills;
using APS.Agent.Core.Strata;

namespace APS.Agent.Cli.Commands;

public sealed record BenchRunOptions(
    string CampaignDir,
    ProviderProtocol Protocol = ProviderProtocol.ChatCompletions,
    Uri? Endpoint = null,
    string? ApiKeyEnvVar = null,
    bool AllowExternalEndpoints = false,
    string? TaskFilter = null,
    int? Limit = null,
    int MaxTurns = 40,
    int ContextBudgetTokens = 65536,
    int? MaxReasoningTokens = null);

public static partial class BenchCommands
{
    [GeneratedRegex(@"[A-Za-z]:[\\/][^\s""'<>|`,;\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9_./:-])/(?:Users|home)/[^\s""'<>|`,;\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex PosixUserPathRegex();

    public static void Freeze(
        string campaignDir,
        string trackKey,
        IReadOnlyList<(string Selector, string Thinking)> models,
        string? suiteRootDir = null)
    {
        if (models.Count == 0)
            throw new ArgumentException("At least one selector:thinking model entry is required.", nameof(models));

        string resolvedSuiteDir = !string.IsNullOrWhiteSpace(suiteRootDir)
            ? Path.GetFullPath(suiteRootDir)
            : Path.GetFullPath(Environment.GetEnvironmentVariable("ADS_BENCH_SUITE_DIR")
                ?? throw new InvalidOperationException("Suite directory not provided (pass --suite-dir or set ADS_BENCH_SUITE_DIR)."));

        IReadOnlyList<TrackSpec> tracks = TrackSpec.Resolve(trackKey);
        string camp = Path.GetFullPath(campaignDir);
        string inputsDir = Path.Combine(camp, "inputs");
        string resultsDir = Path.Combine(camp, "results");
        if (File.Exists(Path.Combine(camp, "campaign.json")))
            throw new InvalidOperationException($"Campaign already frozen at '{camp}'.");

        Directory.CreateDirectory(inputsDir);
        Directory.CreateDirectory(resultsDir);

        string suiteSource = tracks.Count == 1
            ? Path.Combine(resolvedSuiteDir, tracks[0].SuiteFile)
            : Path.Combine(resolvedSuiteDir, "ads-bench-v2.json");

        WriteFrozenSuite(suiteSource, resolvedSuiteDir, Path.Combine(inputsDir, "suite.json"));
        CopyRequiredInput(Path.Combine(resolvedSuiteDir, "scoring-reference.json"), Path.Combine(inputsDir, "scoring-reference.json"));
        CopyRequiredInput(Path.Combine(resolvedSuiteDir, "system_prompt.md"), Path.Combine(inputsDir, "system_prompt.md"));

        foreach (TrackSpec t in tracks)
            CopyRequiredInput(Path.Combine(resolvedSuiteDir, t.BriefFile), Path.Combine(inputsDir, t.BriefFile));

        var inputHashes = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(inputsDir, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
        {
            string rel = Path.GetRelativePath(camp, file).Replace('\\', '/');
            inputHashes[rel] = ComputeSha256(file);
        }

        var modelsArr = new JsonArray();
        foreach (var (selector, thinking) in models)
        {
            modelsArr.Add(new JsonObject
            {
                ["selector"] = selector,
                ["thinking"] = thinking,
            });
        }

        var manifest = new JsonObject
        {
            ["campaign_id"] = Path.GetFileName(camp.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["track"] = trackKey.Trim().ToLowerInvariant(),
            ["input_sha256"] = JsonSerializer.SerializeToNode(inputHashes),
            ["models"] = modelsArr,
        };

        File.WriteAllText(
            Path.Combine(camp, "campaign.json"),
            manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    public static async Task RunAsync(BenchRunOptions opts, TextWriter output, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(opts);
        string camp = Path.GetFullPath(opts.CampaignDir);
        using var manDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "campaign.json"), Encoding.UTF8));
        JsonElement manifest = manDoc.RootElement;
        VerifyInputHashes(camp, manifest);

        StackPaths paths = StackPaths.ResolveFromEnvironment();
        SkillBundle skill = SkillBundleLoader.Load(paths.SkillDir);
        string baseSystemPrompt = File.ReadAllText(Path.Combine(camp, "inputs", "system_prompt.md"), Encoding.UTF8)
            + "\n\n" + skill.CombinedPromptText;

        using var suiteDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "suite.json"), Encoding.UTF8));
        var tasks = suiteDoc.RootElement.GetProperty("tasks").EnumerateArray()
            .Select(t => t.Clone())
            .Where(t => string.IsNullOrWhiteSpace(opts.TaskFilter)
                        || string.Equals(t.GetProperty("id").GetString(), opts.TaskFilter, StringComparison.OrdinalIgnoreCase))
            .Take(opts.Limit ?? int.MaxValue)
            .ToList();

        string campaignTrack = manifest.GetProperty("track").GetString() ?? "ads";
        var tracksByKey = TrackSpec.Resolve("all").ToDictionary(t => t.Key, StringComparer.OrdinalIgnoreCase);

        Uri baseUri = opts.Endpoint ?? OperatorCommands.DefaultLocalEndpoint;
        int? strataNCtx = null;
        if (EndpointGuard.IsLoopbackHost(baseUri.Host))
        {
            StrataModelInfo info = await StrataSupervisor.ProbeRunningAsync(baseUri, ct: ct).ConfigureAwait(false);
            if (info.NCtx > 0) strataNCtx = info.NCtx;
        }

        string resultsDir = Path.Combine(camp, "results");
        Directory.CreateDirectory(resultsDir);

        foreach (var modelEl in manifest.GetProperty("models").EnumerateArray())
        {
            string selector = modelEl.GetProperty("selector").GetString()!;
            string thinking = modelEl.GetProperty("thinking").GetString()!;
            var providerConfig = new ProviderConfig(
                opts.Protocol,
                baseUri,
                selector,
                opts.ApiKeyEnvVar,
                opts.AllowExternalEndpoints,
                opts.MaxReasoningTokens,
                MaxOutputTokens: 8192,
                ReasoningEffort: thinking == "none" ? null : thinking);
            IModelProvider provider = ModelProviderFactory.Create(providerConfig);

            foreach (var tasksInTrack in tasks.GroupBy(t =>
                t.TryGetProperty("track", out var trP) && trP.ValueKind == JsonValueKind.String
                    ? trP.GetString()!
                    : campaignTrack, StringComparer.OrdinalIgnoreCase))
            {
                TrackSpec track = tracksByKey[tasksInTrack.Key];
                string profileDir = paths.ProfileDir(track.Profile);
                string configDigest = HashDictionary(StackOrchestrator.ComputeProfileHashes(profileDir));

                foreach (JsonElement task in tasksInTrack)
                {
                    ct.ThrowIfCancellationRequested();
                    string taskId = task.GetProperty("id").GetString()!;
                    string trialId = SanitizeId($"{selector}-{thinking}-{taskId}");
                    string sidecarPath = Path.Combine(resultsDir, $"{trialId}.json");
                    if (File.Exists(sidecarPath))
                        continue;

                    var (bridgeKey, tier) = ResolveTaskKeyAndTier(task);
                    string bridgeCfg = paths.BridgeConfigPath(bridgeKey);
                    HashSet<string>? allowedTools = ResolveTaskAllowedTools(task);

                    string trialArtifactsDir = Path.Combine(resultsDir, trialId);
                    Directory.CreateDirectory(trialArtifactsDir);
                    string journalRel = $"results/{trialId}.jsonl";
                    string journalPath = Path.Combine(camp, journalRel);

                    OwnedStackHandle stack = StackOrchestrator.StartStack(paths, track);
                    var sw = Stopwatch.StartNew();
                    bool recipesRestored = false;
                    string recipesDir = Path.Combine(profileDir, "Recipes");
                    try
                    {
                        var mcpEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
                        {
                            ["APS_MCP_TELEMETRY_DIR"] = trialArtifactsDir,
                        };
                        await using StdioMcpToolInvoker mcp = await StdioMcpToolInvoker.ConnectAsync(
                            paths.McpBin,
                            ["--config", bridgeCfg],
                            mcpEnv,
                            Path.GetDirectoryName(paths.McpBin),
                            ct).ConfigureAwait(false);

                        await StackOrchestrator.WaitReadyAsync(mcp, TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);

                        var sessionOpts = new SessionOptions(
                            AgentMode.Benchmark,
                            opts.Protocol,
                            baseSystemPrompt,
                            SessionTier: tier,
                            AllowDirectDeviceCommand: false,
                            RequireValidatedUpload: true,
                            BenchmarkAllowedTools: allowedTools,
                            MaxTurns: opts.MaxTurns,
                            ContextBudgetTokens: opts.ContextBudgetTokens,
                            StrataNCtx: strataNCtx,
                            StripHistoricalThinking: true,
                            ConfigDigest: configDigest,
                            SkillDigest: skill.SkillDigestSha256,
                            SkillReadTool: new SandboxedSkillReadTool(paths.SkillDir));

                        string ask = BuildTaskAsk(camp, track, task);
                        var runner = new AgentSessionRunner(provider, mcp, sessionOpts);
                        SessionResult res = await runner.RunAsync(ask, journalPath, ct).ConfigureAwait(false);
                        sw.Stop();

                        JsonElement? finalStatus = await TryReadSystemStatusAsync(mcp, ct).ConfigureAwait(false);
                        StackOrchestrator.RestoreRecipes(recipesDir, stack.BaselineRecipes, trialArtifactsDir);
                        recipesRestored = true;
                        StackOrchestrator.CollectTrialArtifacts(
                            profileDir,
                            trialArtifactsDir,
                            finalStatus,
                            additionalLogsDir: Path.Combine(paths.AppDir, "Logs"));

                        var sidecar = new AuditSidecar(
                            trialId,
                            taskId,
                            selector,
                            thinking,
                            res.Status,
                            res.FinalAnswer,
                            sw.Elapsed.TotalSeconds,
                            res.TotalCostUsd,
                            res.ToolCallCount,
                            configDigest,
                            ComputeSchemaDigest(mcp.Declarations),
                            skill.SkillDigestSha256,
                            journalRel,
                            res.JournalSha256,
                            res.UsageRecords,
                            ErrorCategory: res.ErrorCategory);
                        AuditJournalWriter.WriteSidecar(sidecarPath, sidecar);
                        await output.WriteLineAsync($"[{trialId}] {res.Status} ({sw.Elapsed.TotalSeconds:F1}s, tools={res.ToolCallCount})").ConfigureAwait(false);
                    }
                    finally
                    {
                        if (!recipesRestored)
                        {
                            try { StackOrchestrator.RestoreRecipes(recipesDir, stack.BaselineRecipes, trialArtifactsDir); }
                            catch { }
                        }
                        StackOrchestrator.StopOwnedStack(stack);
                    }
                }
            }
        }
    }

    public static IReadOnlyList<string> Score(string campaignDir, bool batchesOnly = false, int batchSize = 8)
    {
        if (batchesOnly)
            return BenchmarkGrader.MakeBatches(campaignDir, batchSize);
        BenchmarkGrader.MergeScores(campaignDir);
        return [Path.Combine(Path.GetFullPath(campaignDir), "scores.json")];
    }

    public static IReadOnlyList<string> Report(string campaignDir, string? outDir = null) =>
        BenchmarkReportBuilder.WriteReport(campaignDir, outDir);

    public static void FreezeExport(string campaignDir, string exportDir)
    {
        string src = Path.GetFullPath(campaignDir);
        string dst = Path.GetFullPath(exportDir);
        if (string.Equals(src, dst, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export directory must differ from source campaign directory.");

        if (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any())
            throw new InvalidOperationException($"Export directory is not empty: {dst}");

        Directory.CreateDirectory(dst);

        foreach (string sub in new[] { "inputs", "results", "grading" })
        {
            string srcSub = Path.Combine(src, sub);
            if (Directory.Exists(srcSub))
                CopyDirectoryStrict(srcSub, Path.Combine(dst, sub));
        }

        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            if (name == "campaign.json" || name == "scores.json" || name.Contains("-results.", StringComparison.OrdinalIgnoreCase))
                File.Copy(file, Path.Combine(dst, name), overwrite: true);
        }

        foreach (string sub in new[] { "results", "grading" })
        {
            string targetSub = Path.Combine(dst, sub);
            if (!Directory.Exists(targetSub)) continue;
            foreach (string file in Directory.EnumerateFiles(targetSub, "*", SearchOption.AllDirectories))
                SanitizeExportFile(file);
        }

        foreach (string file in Directory.EnumerateFiles(dst, "*", SearchOption.TopDirectoryOnly))
        {
            if (Path.GetFileName(file) != "campaign.json")
                SanitizeExportFile(file);
        }

        ReconcileExportedSidecars(dst);
    }

    private static void ReconcileExportedSidecars(string exportDir)
    {
        string resultsDir = Path.Combine(exportDir, "results");
        if (!Directory.Exists(resultsDir)) return;

        string exportRootPrefix = exportDir.EndsWith(Path.DirectorySeparatorChar)
            ? exportDir
            : exportDir + Path.DirectorySeparatorChar;

        foreach (string sidecarPath in Directory.EnumerateFiles(resultsDir, "*.json", SearchOption.TopDirectoryOnly))
        {
            JsonNode? node = JsonNode.Parse(File.ReadAllText(sidecarPath, Encoding.UTF8));
            if (node is not JsonObject obj) continue;
            if (!obj.TryGetPropertyValue("journal_file", out JsonNode? jfNode) || jfNode is null) continue;

            string rel = jfNode.GetValue<string>();
            string full = Path.GetFullPath(Path.Combine(exportDir, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(exportRootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Sidecar journal_file escapes export dir: {rel}");
            if (!File.Exists(full)) continue;

            obj["journal_sha256"] = ComputeSha256(full);
            File.WriteAllText(
                sidecarPath,
                obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                new UTF8Encoding(false));
        }
    }

    private static void SanitizeExportFile(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        if (ext == ".json")
        {
            string raw = File.ReadAllText(file, Encoding.UTF8);
            try
            {
                JsonNode? node = JsonNode.Parse(raw);
                if (node is not null)
                {
                    JsonNode sanitized = RedactJsonNode(node)!;
                    File.WriteAllText(
                        file,
                        sanitized.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
                        new UTF8Encoding(false));
                    return;
                }
            }
            catch (JsonException) { }
            File.WriteAllText(file, RedactTextPaths(raw), new UTF8Encoding(false));
        }
        else if (ext == ".jsonl")
        {
            var sb = new StringBuilder();
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    JsonNode? node = JsonNode.Parse(line);
                    if (node is not null)
                    {
                        sb.AppendLine(RedactJsonNode(node)!.ToJsonString());
                        continue;
                    }
                }
                catch (JsonException) { }
                sb.AppendLine(RedactTextPaths(line));
            }
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }
        else if (ext is ".md" or ".csv" or ".txt" or ".log" or ".tsv")
        {
            string raw = File.ReadAllText(file, Encoding.UTF8);
            File.WriteAllText(file, RedactTextPaths(raw), new UTF8Encoding(false));
        }
    }

    private static JsonNode? RedactJsonNode(JsonNode? node)
    {
        if (node is null) return null;
        if (node is JsonValue val && val.TryGetValue<string>(out string? s))
            return JsonValue.Create(RedactTextPaths(s));
        if (node is JsonObject obj)
        {
            var copy = new JsonObject();
            foreach (var kv in obj)
                copy[kv.Key] = RedactJsonNode(kv.Value);
            return copy;
        }
        if (node is JsonArray arr)
        {
            var copy = new JsonArray();
            foreach (JsonNode? item in arr)
                copy.Add(RedactJsonNode(item));
            return copy;
        }
        return JsonNode.Parse(node.ToJsonString());
    }

    private static string RedactTextPaths(string text)
    {
        string step1 = WindowsPathRegex().Replace(text, "[REDACTED_PATH]");
        return PosixUserPathRegex().Replace(step1, "[REDACTED_PATH]");
    }

    private static void CopyDirectoryStrict(string srcDir, string dstDir)
    {
        if ((new DirectoryInfo(srcDir).Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Refusing to export reparse point: {srcDir}");
        Directory.CreateDirectory(dstDir);
        foreach (string dir in Directory.EnumerateDirectories(srcDir, "*", SearchOption.TopDirectoryOnly))
            CopyDirectoryStrict(dir, Path.Combine(dstDir, Path.GetFileName(dir)));
        foreach (string file in Directory.EnumerateFiles(srcDir, "*", SearchOption.TopDirectoryOnly))
        {
            if ((new FileInfo(file).Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Refusing to export reparse point: {file}");
            File.Copy(file, Path.Combine(dstDir, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static void CopyRequiredInput(string src, string dst)
    {
        if (!File.Exists(src))
            throw new FileNotFoundException($"Required suite input file not found: {src}", src);
        File.Copy(src, dst, overwrite: true);
    }

    private static void VerifyInputHashes(string camp, JsonElement manifest)
    {
        if (!manifest.TryGetProperty("input_sha256", out var inputs) || inputs.ValueKind != JsonValueKind.Object)
            return;
        string rootPrefix = camp.EndsWith(Path.DirectorySeparatorChar) ? camp : camp + Path.DirectorySeparatorChar;
        foreach (var kv in inputs.EnumerateObject())
        {
            string full = Path.GetFullPath(Path.Combine(camp, kv.Name.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Frozen input path escapes campaign dir: {kv.Name}");
            if (!string.Equals(ComputeSha256(full), kv.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Frozen input hash mismatch: {kv.Name}");
        }
    }

    private static void WriteFrozenSuite(string suiteSource, string resolvedSuiteDir, string suiteDest)
    {
        if (!File.Exists(suiteSource))
            throw new FileNotFoundException($"Required suite input file not found: {suiteSource}", suiteSource);

        string rawSuite = File.ReadAllText(suiteSource, Encoding.UTF8);
        string fixturesDir = Path.Combine(Path.GetDirectoryName(resolvedSuiteDir) ?? "", "fixtures");
        string fixturesIndexPath = Path.Combine(fixturesDir, "fixtures.json");
        if (!File.Exists(fixturesIndexPath))
        {
            File.WriteAllText(suiteDest, rawSuite, new UTF8Encoding(false));
            return;
        }

        using var fixturesDoc = StrictJson.Parse(File.ReadAllText(fixturesIndexPath, Encoding.UTF8));
        if (!fixturesDoc.RootElement.TryGetProperty("attachments", out JsonElement attachmentsIndex)
            || attachmentsIndex.ValueKind != JsonValueKind.Object)
        {
            File.WriteAllText(suiteDest, rawSuite, new UTF8Encoding(false));
            return;
        }

        JsonNode suiteNode = JsonNode.Parse(rawSuite)
            ?? throw new InvalidOperationException($"Invalid suite JSON: {suiteSource}");
        if (suiteNode["tasks"] is JsonArray tasksArr)
        {
            foreach (JsonNode? tNode in tasksArr)
            {
                if (tNode is not JsonObject taskObj) continue;
                if (taskObj["attachments"] is not JsonArray attArr || attArr.Count == 0) continue;

                string ask = taskObj["ask"]?.GetValue<string>() ?? "";
                foreach (JsonNode? attIdNode in attArr)
                {
                    string attId = attIdNode?.GetValue<string>()
                        ?? throw new InvalidOperationException("Attachment id must be a string.");
                    if (!attachmentsIndex.TryGetProperty(attId, out JsonElement meta) || meta.ValueKind != JsonValueKind.Object)
                        throw new InvalidOperationException($"Unknown attachment '{attId}' in '{fixturesIndexPath}'.");
                    string fileName = meta.GetProperty("file").GetString()!;
                    string expectedSha = meta.GetProperty("sha256").GetString()!;
                    string fixturePath = Path.GetFullPath(Path.Combine(fixturesDir, fileName));
                    string dirPrefix = Path.GetFullPath(fixturesDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!fixturePath.StartsWith(dirPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(fixturePath))
                        throw new InvalidOperationException($"Missing fixture file for attachment '{attId}': {fileName}");
                    byte[] data = File.ReadAllBytes(fixturePath);
                    string actualSha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
                    if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Fixture SHA-256 mismatch for '{attId}' ({fileName}).");
                    string text = Encoding.UTF8.GetString(data);
                    ask += "\n\nAttached file: " + fileName + "\n\n" + FenceText(text);
                }
                taskObj["ask"] = ask;
            }
        }

        File.WriteAllText(
            suiteDest,
            suiteNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n",
            new UTF8Encoding(false));
    }

    private static string FenceText(string text)
    {
        string fence = "```";
        while (text.Contains(fence, StringComparison.Ordinal))
            fence += "`";
        return fence + "\n" + text + (text.EndsWith('\n') ? "" : "\n") + fence;
    }

    private static string BuildTaskAsk(string camp, TrackSpec track, JsonElement task)
    {
        string ask = task.GetProperty("ask").GetString() ?? "";
        string taskId = task.GetProperty("id").GetString() ?? "";
        if (taskId is "SC1" or "AC1" or "CC1")
        {
            string briefPath = Path.Combine(camp, "inputs", track.BriefFile);
            if (File.Exists(briefPath))
            {
                string brief = File.ReadAllText(briefPath, Encoding.UTF8);
                if (!ask.Contains(brief.Trim(), StringComparison.Ordinal))
                    ask += "\n\n" + brief;
            }
        }
        return ask;
    }

    public static (string BridgeKey, string SessionTier) ResolveTaskKeyAndTier(JsonElement task)
    {
        if (task.TryGetProperty("key", out var kp) && kp.ValueKind == JsonValueKind.String)
        {
            return kp.GetString()!.Trim().ToLowerInvariant() switch
            {
                "read" => ("read", "Read"),
                "recipe" => ("recipe", "Recipe"),
                "manual" => ("manual", "Manual"),
                var other => throw new InvalidOperationException($"Unknown task key '{other}'."),
            };
        }
        if (task.TryGetProperty("mode", out var mp) && mp.ValueKind == JsonValueKind.String)
        {
            return mp.GetString()!.Trim().ToLowerInvariant() switch
            {
                "read" => ("read", "Read"),
                "recipe" => ("recipe", "Recipe"),
                "manual" or "full" => ("manual", "Manual"),
                var other => ("read", other),
            };
        }
        return ("read", "Read");
    }

    public static HashSet<string>? ResolveTaskAllowedTools(JsonElement task)
    {
        JsonElement arr;
        if (task.TryGetProperty("tools", out arr) && arr.ValueKind == JsonValueKind.Array)
            return ReadStringSet(arr);
        if (task.TryGetProperty("allowed_tools", out arr) && arr.ValueKind == JsonValueKind.Array)
            return ReadStringSet(arr);
        return null;
    }

    private static HashSet<string> ReadStringSet(JsonElement arr) =>
        arr.EnumerateArray()
            .Select(e => e.GetString())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.Ordinal)!;

    private static async Task<JsonElement?> TryReadSystemStatusAsync(IMcpToolInvoker mcp, CancellationToken ct)
    {
        try
        {
            ToolCallReceipt snap = await mcp.CallAsync("system_status", JsonDocument.Parse("{}").RootElement.Clone(), ct)
                .ConfigureAwait(false);
            if (snap.IsError || string.IsNullOrWhiteSpace(snap.Text)) return null;
            using var doc = JsonDocument.Parse(snap.Text);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static string ComputeSha256(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private static string HashDictionary(Dictionary<string, string> hashes)
    {
        string serialized = JsonSerializer.Serialize(hashes.OrderBy(kv => kv.Key, StringComparer.Ordinal));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serialized))).ToLowerInvariant();
    }

    internal static string ComputeSchemaDigest(IReadOnlyList<Core.Schemas.ToolDeclaration> declarations)
    {
        var canonical = declarations
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .Select(d => new { d.Name, d.Description, schema = d.Parameters.GetRawText() });
        string json = JsonSerializer.Serialize(canonical);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private static string SanitizeId(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        return sb.ToString();
    }
}
