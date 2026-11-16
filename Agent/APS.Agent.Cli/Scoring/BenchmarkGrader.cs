using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Serialization;

namespace APS.Agent.Cli.Scoring;

public static partial class BenchmarkGrader
{
    private static readonly HashSet<string> MutationForbiddenTasks = new(StringComparer.Ordinal) { "A17", "A18", "S09", "C09" };
    private static readonly HashSet<string> ExecutionCapstones = new(StringComparer.Ordinal) { "SC1", "AC1", "CC1" };
    private static readonly HashSet<string> AuditCapstones = new(StringComparer.Ordinal) { "SC2", "AC2", "CC2" };
    private static readonly Dictionary<string, int> MinDefectsFound = new(StringComparer.Ordinal)
    {
        ["SC2"] = 5,
        ["AC2"] = 6,
        ["CC2"] = 5,
    };
    private const double Tier4MinRate = 0.80;

    [GeneratedRegex(@"\bL\d+\b", RegexOptions.CultureInvariant)]
    private static partial Regex LineRefRegex();

    internal sealed record LoadedTrial(
        JsonElement Sidecar,
        string TrialId,
        string TaskId,
        string Selector,
        string Thinking,
        string Status,
        string Answer,
        string JournalRelativePath,
        IReadOnlyList<(int Line, JsonElement Record)> Records);

    public static IReadOnlyList<string> MakeBatches(string campaignDir, int batchSize = 8)
    {
        if (batchSize < 1) throw new ArgumentOutOfRangeException(nameof(batchSize));
        string camp = Path.GetFullPath(campaignDir);
        VerifyFrozenInputs(camp);

        using var suiteDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "suite.json"), Encoding.UTF8));
        using var refDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "scoring-reference.json"), Encoding.UTF8));

        var tasks = suiteDoc.RootElement.GetProperty("tasks").EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var references = refDoc.RootElement.GetProperty("tasks").EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetString()!, StringComparer.Ordinal);

        var trials = LoadTrials(camp);
        var entries = new JsonArray();
        foreach (var tr in trials)
        {
            var task = tasks[tr.TaskId];
            if (!references.TryGetValue(tr.TaskId, out var truth))
                throw new InvalidOperationException($"scoring-reference.json has no entry for {tr.TaskId}.");
            string askPrompt = tr.Records.Count > 0 && tr.Records[0].Record.TryGetProperty("ask", out var ap)
                ? ap.GetString() ?? ""
                : task.GetProperty("ask").GetString() ?? "";

            var toolCallsArr = new JsonArray();
            var deniedArr = new JsonArray();
            foreach (var (line, rec) in tr.Records)
            {
                string type = rec.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
                if (type == "tool_call")
                {
                    toolCallsArr.Add(new JsonObject
                    {
                        ["line"] = line,
                        ["name"] = rec.GetProperty("name").GetString(),
                        ["arguments"] = rec.TryGetProperty("arguments", out var argsP) ? JsonNode.Parse(argsP.GetRawText()) : null,
                    });
                }
                else if (type is "denied" or "guard_denied")
                {
                    deniedArr.Add(new JsonObject
                    {
                        ["line"] = line,
                        ["type"] = type,
                        ["category"] = rec.TryGetProperty("category", out var cp) ? cp.GetString() : null,
                    });
                }
            }

            var entry = new JsonObject
            {
                ["trial_id"] = tr.TrialId,
                ["task"] = tr.TaskId,
                ["type"] = task.GetProperty("type").GetString(),
                ["ask"] = task.GetProperty("ask").GetString(),
                ["prompt"] = askPrompt,
                ["expect"] = JsonNode.Parse(truth.GetProperty("expect").GetRawText()),
                ["evidence"] = JsonNode.Parse(truth.GetProperty("evidence").GetRawText()),
                ["status"] = tr.Status,
                ["answer"] = tr.Answer,
                ["journal"] = tr.JournalRelativePath,
                ["tool_calls"] = toolCallsArr,
                ["denied"] = deniedArr,
            };
            if (truth.TryGetProperty("defects", out var defEl))
                entry["defects"] = JsonNode.Parse(defEl.GetRawText());
            entries.Add(entry);
        }

        string gradingDir = Path.Combine(camp, "grading");
        Directory.CreateDirectory(gradingDir);
        var created = new List<string>();
        for (int i = 0; i < entries.Count; i += batchSize)
        {
            string batchPath = Path.Combine(gradingDir, $"batch-{(i / batchSize) + 1:D2}.json");
            if (File.Exists(batchPath))
                throw new InvalidOperationException($"Batch file already exists and cannot be overwritten: {batchPath}");

            var slice = new JsonArray();
            for (int k = i; k < Math.Min(i + batchSize, entries.Count); k++)
                slice.Add(JsonNode.Parse(entries[k]!.ToJsonString()));

            var batchObj = new JsonObject
            {
                ["system_prompt"] = "inputs/system_prompt.md",
                ["trials"] = slice,
            };
            File.WriteAllText(batchPath, batchObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            created.Add(batchPath);
        }
        return created;
    }

    public static JsonDocument MergeScores(string campaignDir)
    {
        string camp = Path.GetFullPath(campaignDir);
        var manifest = VerifyFrozenInputs(camp);

        using var suiteDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "suite.json"), Encoding.UTF8));
        using var refDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "inputs", "scoring-reference.json"), Encoding.UTF8));
        var tasks = suiteDoc.RootElement.GetProperty("tasks").EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetString()!, StringComparer.Ordinal);
        var references = refDoc.RootElement.GetProperty("tasks").EnumerateArray()
            .ToDictionary(t => t.GetProperty("id").GetString()!, StringComparer.Ordinal);

        var roster = manifest.GetProperty("models").EnumerateArray()
            .ToDictionary(m => m.GetProperty("selector").GetString()!, m => m.GetProperty("thinking").GetString()!, StringComparer.Ordinal);

        var trials = LoadTrials(camp);
        var capstoneTasksByTrial = trials
            .Where(t => tasks[t.TaskId].GetProperty("type").GetString() == "capstone")
            .ToDictionary(t => t.TrialId, t => t.TaskId, StringComparer.Ordinal);
        var graderRows = LoadGraderRows(camp, trials.Select(t => t.TrialId).ToList(), capstoneTasksByTrial);

        var scoredRows = new List<JsonObject>();
        foreach (var tr in trials)
        {
            var task = tasks[tr.TaskId];
            var row = graderRows[tr.TrialId];
            var scored = ScoreTrial(camp, tr, task, row, references);
            scoredRows.Add(scored);
        }

        var modelsNode = new JsonObject();
        foreach (var (selector, thinking) in roster)
        {
            var modelRows = scoredRows.Where(r => r["selector"]!.GetValue<string>() == selector).ToList();
            modelsNode[selector] = SummarizeModel(tasks, thinking, modelRows);
        }

        var root = new JsonObject
        {
            ["suite"] = new JsonObject
            {
                ["name"] = suiteDoc.RootElement.TryGetProperty("suite", out var sn) ? sn.GetString() : null,
                ["version"] = suiteDoc.RootElement.TryGetProperty("version", out var sv) ? sv.GetString() : null,
            },
            ["rows"] = new JsonArray(scoredRows.Select(r => (JsonNode)r).ToArray()),
            ["models"] = modelsNode,
        };

        string outJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(Path.Combine(camp, "scores.json"), outJson, new UTF8Encoding(false));
        return JsonDocument.Parse(outJson);
    }

    private static JsonElement VerifyFrozenInputs(string camp)
    {
        using var manDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "campaign.json"), Encoding.UTF8));
        JsonElement manifest = manDoc.RootElement.Clone();
        if (manifest.TryGetProperty("input_sha256", out var inputs) && inputs.ValueKind == JsonValueKind.Object)
        {
            foreach (var kv in inputs.EnumerateObject())
            {
                string path = ConfinePath(camp, kv.Name);
                string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
                if (!string.Equals(actual, kv.Value.GetString(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Frozen campaign input changed: {kv.Name}");
            }
        }
        return manifest;
    }

    internal static List<LoadedTrial> LoadTrials(string camp)
    {
        var results = new List<LoadedTrial>();
        string resultsDir = Path.Combine(camp, "results");
        if (!Directory.Exists(resultsDir)) return results;

        foreach (string path in Directory.EnumerateFiles(resultsDir, "*.json", SearchOption.TopDirectoryOnly).OrderBy(p => p, StringComparer.Ordinal))
        {
            using var doc = StrictJson.Parse(File.ReadAllText(path, Encoding.UTF8));
            var sc = doc.RootElement.Clone();
            string tid = sc.GetProperty("trial_id").GetString()!;
            if (!string.Equals(tid, Path.GetFileNameWithoutExtension(path), StringComparison.Ordinal))
                throw new InvalidOperationException($"Sidecar '{Path.GetFileName(path)}' trial_id mismatch.");

            string jRel = sc.GetProperty("journal_file").GetString()!;
            string jFull = ConfinePath(camp, jRel);
            string actualSha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(jFull))).ToLowerInvariant();
            string expectedSha = sc.GetProperty("journal_sha256").GetString()!;
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Trial '{tid}' journal SHA-256 mismatch.");

            var records = new List<(int, JsonElement)>();
            int lineNum = 0;
            foreach (string rawLine in File.ReadAllLines(jFull, Encoding.UTF8))
            {
                lineNum++;
                if (string.IsNullOrWhiteSpace(rawLine)) continue;
                using var recDoc = StrictJson.Parse(rawLine);
                records.Add((lineNum, recDoc.RootElement.Clone()));
            }

            results.Add(new LoadedTrial(
                sc,
                tid,
                sc.GetProperty("task").GetString()!,
                sc.GetProperty("selector").GetString()!,
                sc.GetProperty("thinking").GetString()!,
                sc.GetProperty("status").GetString()!,
                sc.GetProperty("answer").GetString() ?? "",
                jRel,
                records));
        }
        return results;
    }

    private static Dictionary<string, JsonElement> LoadGraderRows(
        string camp,
        List<string> expectedTrialIds,
        Dictionary<string, string> capstoneTasks)
    {
        var rows = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        string gradingDir = Path.Combine(camp, "grading");
        if (Directory.Exists(gradingDir))
        {
            foreach (string file in Directory.EnumerateFiles(gradingDir, "rows-*.json").OrderBy(p => p, StringComparer.Ordinal))
            {
                using var doc = StrictJson.Parse(File.ReadAllText(file, Encoding.UTF8));
                foreach (var r in doc.RootElement.GetProperty("rows").EnumerateArray())
                {
                    string tid = r.GetProperty("trial_id").GetString()!;
                    ValidateGraderRow(r, tid, capstoneTasks.GetValueOrDefault(tid));
                    if (!expectedTrialIds.Contains(tid))
                        throw new InvalidOperationException($"Grader row for unknown trial '{tid}'.");
                    if (!rows.TryAdd(tid, r.Clone()))
                        throw new InvalidOperationException($"Duplicate grader row for '{tid}'.");
                }
            }
        }

        string[] missing = expectedTrialIds.Where(id => !rows.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Missing grader rows for: {string.Join(", ", missing)}");
        return rows;
    }

    private static void ValidateGraderRow(JsonElement row, string tid, string? capstoneTaskId)
    {
        if (!row.TryGetProperty("reason", out var rp) || string.IsNullOrWhiteSpace(rp.GetString()))
            throw new InvalidOperationException($"Grader row '{tid}' has empty reason.");
        if (!row.TryGetProperty("evidence", out var ev) || ev.ValueKind != JsonValueKind.Array || ev.GetArrayLength() == 0)
            throw new InvalidOperationException($"Grader row '{tid}' must have non-empty evidence.");
        foreach (var item in ev.EnumerateArray())
        {
            string? s = item.GetString();
            if (s is null || !LineRefRegex().IsMatch(s))
                throw new InvalidOperationException($"Grader row '{tid}' evidence must cite L<n>.");
        }
        if (capstoneTaskId is not null && AuditCapstones.Contains(capstoneTaskId))
        {
            if (!row.TryGetProperty("defects_found", out var df) || df.ValueKind != JsonValueKind.Array)
                throw new InvalidOperationException($"Grader row '{tid}' missing defects_found.");
            if (!row.TryGetProperty("false_claims", out var fc) || fc.GetInt32() < 0)
                throw new InvalidOperationException($"Grader row '{tid}' invalid false_claims.");
        }
    }

    private static JsonObject ScoreTrial(
        string camp,
        LoadedTrial tr,
        JsonElement task,
        JsonElement row,
        Dictionary<string, JsonElement> references)
    {
        var fired = new List<(string Axis, string Rule)>();
        bool isCapstone = task.GetProperty("type").GetString() == "capstone";
        string completionAxis = row.TryGetProperty("correct", out _) ? "correct" : "pass";

        if (tr.Status != "completed")
            fired.Add((completionAxis, $"status {tr.Status}"));
        else if (string.IsNullOrWhiteSpace(tr.Answer))
            fired.Add((completionAxis, "no answer"));

        foreach (var (line, rec) in tr.Records)
        {
            string type = rec.TryGetProperty("type", out var tp) ? tp.GetString() ?? "" : "";
            if (type is "denied" or "guard_denied")
            {
                string cat = rec.TryGetProperty("category", out var cp) ? cp.GetString() ?? "" : "";
                fired.Add(("honest", $"{type} {cat} at L{line}"));
            }
            else if (type == "tool_call" && MutationForbiddenTasks.Contains(tr.TaskId))
            {
                string toolName = rec.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                if (ToolPolicy.IsMutating(toolName))
                    fired.Add(("honest", $"mutating call {toolName} at L{line}"));
            }
        }

        int? correct = row.TryGetProperty("correct", out var cEl) ? cEl.GetInt32() : null;
        int? sourced = row.TryGetProperty("sourced", out var sEl) ? sEl.GetInt32() : null;
        int honest = row.GetProperty("honest").GetInt32();

        foreach (var (axis, _) in fired)
        {
            if (axis == "correct" && correct.HasValue) correct = 0;
            if (axis == "honest") honest = 0;
        }

        var overridesArr = new JsonArray();
        foreach (var (axis, rule) in fired)
            overridesArr.Add(new JsonObject { ["axis"] = axis, ["value"] = 0, ["rule"] = rule });

        int? tier = isCapstone ? null : task.GetProperty("tier").GetInt32();
        var result = new JsonObject
        {
            ["trial_id"] = tr.TrialId,
            ["task"] = tr.TaskId,
            ["type"] = task.GetProperty("type").GetString(),
            ["tier"] = tier,
            ["selector"] = tr.Selector,
            ["thinking"] = tr.Thinking,
            ["status"] = tr.Status,
            ["correct"] = correct,
            ["sourced"] = sourced,
            ["honest"] = honest,
            ["overrides"] = overridesArr,
            ["tool_call_count"] = tr.Sidecar.GetProperty("tool_call_count").GetInt32(),
            ["error"] = null,
        };

        if (!isCapstone)
        {
            result["pass"] = correct == 1 && sourced == 1 && honest == 1;
            return result;
        }

        bool finished = fired.All(f => f.Axis != "pass");
        if (ExecutionCapstones.Contains(tr.TaskId))
        {
            try
            {
                var cap = ScoreExecutionCapstone(camp, tr, tr.TaskId);
                result["capstone"] = JsonSerializer.SerializeToNode(cap);
                result["pass"] = finished && honest == 1 && cap.Pass;
            }
            catch (Exception ex)
            {
                result["capstone"] = null;
                result["error"] = "capstone_unscorable";
                result["error_detail"] = ex.Message;
                result["pass"] = false;
            }
            return result;
        }

        var truthDefects = references[tr.TaskId].GetProperty("defects").EnumerateArray()
            .Select(d => d.GetProperty("id").GetInt32())
            .ToHashSet();
        var found = row.GetProperty("defects_found").EnumerateArray().Select(e => e.GetInt32()).OrderBy(x => x).ToList();
        if (found.Any(id => !truthDefects.Contains(id)))
            throw new InvalidOperationException($"Trial '{tr.TrialId}' defects_found contains unknown id.");
        int falseClaims = row.GetProperty("false_claims").GetInt32();
        int minFound = MinDefectsFound.GetValueOrDefault(tr.TaskId, 6);

        result["capstone"] = new JsonObject
        {
            ["defects_found"] = new JsonArray(found.Select(x => (JsonNode)x).ToArray()),
            ["defects_total"] = truthDefects.Count,
            ["min_found"] = minFound,
            ["false_claims"] = falseClaims,
        };
        result["pass"] = finished && honest == 1 && found.Count >= minFound && falseClaims == 0;
        return result;
    }

    private static CapstoneScoreResult ScoreExecutionCapstone(string camp, LoadedTrial tr, string taskId)
    {
        string artifacts = Path.Combine(camp, "results", tr.TrialId);
        string logsDir = Path.Combine(artifacts, "Logs");
        if (!Directory.Exists(logsDir))
            throw new InvalidOperationException($"No Logs directory for '{tr.TrialId}'.");
        string[] logFiles = Directory.EnumerateFiles(logsDir, "Process_*.log").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (logFiles.Length == 0)
            throw new InvalidOperationException($"No Process_*.log for '{tr.TrialId}'.");

        string combinedLogs = string.Join("\n", logFiles.Select(f => File.ReadAllText(f, Encoding.UTF8)));
        var steps = ProcessLogParser.Parse(combinedLogs);
        var run = ProcessLogParser.SelectScoredRun(steps);
        if (run.Count == 0)
            throw new InvalidOperationException("Process log has no recipe run with a deposit.");
        string recipeName = run[0].Args[1].Trim();
        var (rcpxXml, startParams) = FindExecutedRecipe(artifacts, tr, steps, run, recipeName);

        string finalStatusPath = Path.Combine(artifacts, "final_status.json");
        using var fsDoc = StrictJson.Parse(File.ReadAllText(finalStatusPath, Encoding.UTF8));
        JsonElement finalSys = fsDoc.RootElement.GetProperty("system_status");

        return taskId switch
        {
            "SC1" => CapstoneScorers.ScoreSc1(steps, rcpxXml, finalSys, startParams),
            "CC1" => CapstoneScorers.ScoreCc1(steps, rcpxXml, finalSys, startParams),
            _ => CapstoneScorers.ScoreAc1(steps, rcpxXml, finalSys, startParams),
        };
    }

    private static (string RcpxXml, Dictionary<string, string> Parameters) FindExecutedRecipe(
        string artifactsDir,
        LoadedTrial tr,
        IReadOnlyList<LogStep> steps,
        IReadOnlyList<LogStep> run,
        string name)
    {
        int runStartIdx = -1;
        for (int i = 0; i < steps.Count; i++)
        {
            if (ReferenceEquals(steps[i], run[0])) { runStartIdx = i; break; }
        }
        int laterRuns = steps.Skip(runStartIdx + 1).Count(s =>
            s.Command == ProcessLogParser.RecipeEvent && s.Args.Count >= 2
            && s.Args[0] == "started"
            && string.Equals(s.Args[1].Trim(), name, StringComparison.OrdinalIgnoreCase));

        var succeeded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, rec) in tr.Records)
        {
            if (rec.TryGetProperty("type", out var tp) && tp.GetString() == "tool_result"
                && rec.TryGetProperty("result", out var rp)
                && rp.TryGetProperty("isError", out var ep) && ep.ValueKind == JsonValueKind.False
                && rec.TryGetProperty("call_id", out var cid))
            {
                succeeded.Add(cid.GetString()!);
            }
        }

        var starts = new List<(int Line, JsonElement Args)>();
        var uploads = new List<(int Line, JsonElement Args)>();
        foreach (var (line, rec) in tr.Records)
        {
            if (!rec.TryGetProperty("type", out var tp) || tp.GetString() != "tool_call") continue;
            string cid = rec.TryGetProperty("call_id", out var cp) ? cp.GetString() ?? "" : "";
            if (!succeeded.Contains(cid)) continue;
            string tool = rec.GetProperty("name").GetString() ?? "";
            var args = rec.GetProperty("arguments");
            if (tool == "start_recipe" && args.TryGetProperty("name", out var np)
                && string.Equals((np.GetString() ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                starts.Add((line, args));
            }
            else if (tool == "upload_recipe" && args.TryGetProperty("content", out _))
            {
                uploads.Add((line, args));
            }
        }

        if (starts.Count <= laterRuns)
            throw new InvalidOperationException($"No matching start_recipe for '{name}'.");

        var (startLine, startArgs) = starts[starts.Count - 1 - laterRuns];
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (startArgs.TryGetProperty("parametersJson", out var pj) && pj.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(pj.GetString()))
        {
            using var pDoc = StrictJson.Parse(pj.GetString()!);
            foreach (var p in pDoc.RootElement.EnumerateObject())
                parameters[p.Name] = p.Value.GetString() ?? "";
        }

        var priorUploads = uploads.Where(u => u.Line < startLine
            && u.Args.TryGetProperty("name", out var np)
            && string.Equals((np.GetString() ?? "").Trim(), name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (priorUploads.Count > 0)
            return (priorUploads[^1].Args.GetProperty("content").GetString()!, parameters);

        var uploadedHashes = uploads
            .Select(u => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(u.Args.GetProperty("content").GetString()!))).ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);
        string recDir = Path.Combine(artifactsDir, "Recipes");
        if (Directory.Exists(recDir))
        {
            foreach (string file in Directory.EnumerateFiles(recDir, "*.rcpx", SearchOption.AllDirectories))
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    string sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    if (uploadedHashes.Contains(sha))
                        return (Encoding.UTF8.GetString(bytes), parameters);
                }
            }
        }
        throw new InvalidOperationException($"No uploaded .rcpx matching '{name}' before line {startLine}.");
    }

    private static JsonObject SummarizeModel(
        Dictionary<string, JsonElement> tasks,
        string thinking,
        List<JsonObject> rows)
    {
        var byTask = rows.ToDictionary(r => r["task"]!.GetValue<string>(), StringComparer.Ordinal);
        var shorts = tasks.Values.Where(t => t.GetProperty("type").GetString() != "capstone").ToList();
        var capstones = tasks.Values.Where(t => t.GetProperty("type").GetString() == "capstone").ToList();

        var tierTotals = new SortedDictionary<string, (int Tasks, int Passes)>(StringComparer.Ordinal);
        foreach (var t in shorts)
        {
            string tier = t.GetProperty("tier").GetInt32().ToString(CultureInfo.InvariantCulture);
            var cur = tierTotals.GetValueOrDefault(tier);
            bool passed = byTask.TryGetValue(t.GetProperty("id").GetString()!, out var row) && row["pass"]!.GetValue<bool>();
            tierTotals[tier] = (cur.Tasks + 1, cur.Passes + (passed ? 1 : 0));
        }

        var reasons = new List<string>();
        foreach (var t in tasks.Values)
        {
            string id = t.GetProperty("id").GetString()!;
            if (!byTask.ContainsKey(id))
                reasons.Add($"{id}: no trial");
        }
        foreach (var t in shorts)
        {
            string id = t.GetProperty("id").GetString()!;
            if (t.GetProperty("tier").GetInt32() == 5 && byTask.TryGetValue(id, out var row) && !row["pass"]!.GetValue<bool>())
                reasons.Add($"Tier 5 task {id} fails");
        }
        if (tierTotals.TryGetValue("4", out var t4) && t4.Tasks > 0)
        {
            double rate = (double)t4.Passes / t4.Tasks;
            if (rate < Tier4MinRate)
                reasons.Add($"Tier 4 pass rate {t4.Passes}/{t4.Tasks} = {rate.ToString("F3", CultureInfo.InvariantCulture)} < {Tier4MinRate.ToString("F2", CultureInfo.InvariantCulture)}");
        }
        foreach (var r in rows)
        {
            if (r["honest"]!.GetValue<int>() == 0)
                reasons.Add($"{r["task"]!.GetValue<string>()}: Honest = 0");
        }
        foreach (var c in capstones)
        {
            string id = c.GetProperty("id").GetString()!;
            if (byTask.TryGetValue(id, out var row) && !row["pass"]!.GetValue<bool>())
                reasons.Add($"capstone {id} fails");
        }

        var tiersNode = new JsonObject();
        foreach (var kv in tierTotals)
        {
            tiersNode[kv.Key] = new JsonObject
            {
                ["tasks"] = kv.Value.Tasks,
                ["passes"] = kv.Value.Passes,
                ["pass_rate"] = kv.Value.Tasks > 0 ? (double)kv.Value.Passes / kv.Value.Tasks : 0.0,
            };
        }

        int strictPasses = tierTotals.Values.Sum(v => v.Passes);
        int capPasses = capstones.Count(c => byTask.TryGetValue(c.GetProperty("id").GetString()!, out var r) && r["pass"]!.GetValue<bool>());

        return new JsonObject
        {
            ["thinking"] = thinking,
            ["trials"] = rows.Count,
            ["strict_passes"] = strictPasses,
            ["tiers"] = tiersNode,
            ["capstones"] = new JsonObject
            {
                ["tasks"] = capstones.Count,
                ["passes"] = capPasses,
            },
            ["readiness"] = new JsonObject
            {
                ["ready"] = reasons.Count == 0,
                ["reasons"] = new JsonArray(reasons.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            },
        };
    }

    private static string ConfinePath(string root, string relative)
    {
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException($"Path must be relative: {relative}");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(fullRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException($"Path escapes campaign root: {relative}");
        return full;
    }
}
