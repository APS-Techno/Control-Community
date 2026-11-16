using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APS.Agent.Core.Serialization;

namespace APS.Agent.Cli.Scoring;

public static class BenchmarkReportBuilder
{
    public static IReadOnlyList<string> WriteReport(string campaignDir, string? outDir = null)
    {
        string camp = Path.GetFullPath(campaignDir);
        string targetDir = Path.GetFullPath(outDir ?? camp);
        Directory.CreateDirectory(targetDir);

        string campName = Path.GetFileName(camp.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        using var scoresDoc = StrictJson.Parse(File.ReadAllText(Path.Combine(camp, "scores.json"), Encoding.UTF8));
        var scores = scoresDoc.RootElement;
        var trials = BenchmarkGrader.LoadTrials(camp).ToDictionary(t => t.TrialId, StringComparer.Ordinal);

        var rowsByModel = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var m in scores.GetProperty("models").EnumerateObject())
            rowsByModel[m.Name] = [];
        foreach (var r in scores.GetProperty("rows").EnumerateArray())
            rowsByModel[r.GetProperty("selector").GetString()!].Add(r);

        var bestRows = new JsonArray();
        var capstonesNode = new JsonObject();
        var tiersNode = new JsonObject();
        var readinessNode = new JsonObject();

        foreach (var m in scores.GetProperty("models").EnumerateObject())
        {
            string sel = m.Name;
            var modelObj = m.Value;
            var modelRows = rowsByModel[sel];
            var shortTrials = modelRows
                .Where(r => r.GetProperty("type").GetString() != "capstone")
                .Select(r => trials[r.GetProperty("trial_id").GetString()!])
                .ToList();

            var durations = shortTrials.Select(t => t.Sidecar.GetProperty("duration_s").GetDouble()).OrderBy(x => x).ToList();
            double? medianS = durations.Count == 0
                ? null
                : (durations.Count % 2 == 1 ? durations[durations.Count / 2] : 0.5 * (durations[durations.Count / 2 - 1] + durations[durations.Count / 2]));

            bestRows.Add(new JsonObject
            {
                ["model"] = sel,
                ["model_id"] = sel,
                ["effort"] = modelObj.GetProperty("thinking").GetString(),
                ["completed"] = shortTrials.Count(t => t.Status == "completed"),
                ["short_tasks"] = modelRows.Count(r => r.GetProperty("type").GetString() != "capstone"),
                ["strict_passes"] = modelObj.GetProperty("strict_passes").GetInt32(),
                ["summed_task_seconds"] = durations.Sum(),
                ["median_task_seconds"] = medianS,
                ["cost_usd"] = shortTrials.Sum(t => t.Sidecar.GetProperty("catalog_estimated_cost_usd").GetDouble()),
                ["tool_calls"] = shortTrials.Sum(t => t.Sidecar.GetProperty("tool_call_count").GetInt32()),
            });

            var capRows = new JsonArray();
            foreach (var r in modelRows.Where(x => x.GetProperty("type").GetString() == "capstone"))
            {
                capRows.Add(new JsonObject
                {
                    ["id"] = r.GetProperty("task").GetString(),
                    ["trial_id"] = r.GetProperty("trial_id").GetString(),
                    ["status"] = r.GetProperty("status").GetString(),
                    ["pass"] = r.GetProperty("pass").GetBoolean(),
                    ["honest"] = r.GetProperty("honest").GetInt32(),
                    ["error"] = r.TryGetProperty("error", out var ep) && ep.ValueKind == JsonValueKind.String ? ep.GetString() : null,
                });
            }

            capstonesNode[sel] = new JsonObject
            {
                ["tasks"] = modelObj.GetProperty("capstones").GetProperty("tasks").GetInt32(),
                ["passes"] = modelObj.GetProperty("capstones").GetProperty("passes").GetInt32(),
                ["rows"] = capRows,
            };
            tiersNode[sel] = JsonNode.Parse(modelObj.GetProperty("tiers").GetRawText());
            readinessNode[sel] = JsonNode.Parse(modelObj.GetProperty("readiness").GetRawText());
        }

        var reportObj = new JsonObject
        {
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["scope"] = $"Campaign {campName}",
            ["campaign"] = campName,
            ["best_rows"] = bestRows,
            ["tiers"] = tiersNode,
            ["capstones"] = capstonesNode,
            ["readiness"] = readinessNode,
        };

        string stem = $"{campName}-results";
        string mdPath = Path.Combine(targetDir, $"{stem}.md");
        string csvPath = Path.Combine(targetDir, $"{stem}.csv");
        string jsonPath = Path.Combine(targetDir, $"{stem}.json");

        File.WriteAllText(mdPath, RenderMarkdown(campName, bestRows, capstonesNode, readinessNode), new UTF8Encoding(false));
        File.WriteAllText(csvPath, RenderCsv(bestRows, capstonesNode, readinessNode), new UTF8Encoding(false));
        File.WriteAllText(jsonPath, reportObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));

        return [mdPath, csvPath, jsonPath];
    }

    private static string RenderMarkdown(string campName, JsonArray bestRows, JsonObject capstones, JsonObject readiness)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {campName} results");
        sb.AppendLine();
        sb.AppendLine("| Model | Ready | Strict passes | Capstones | Cost USD | Tool calls |");
        sb.AppendLine("|---|---|---|---|---|---|");
        foreach (var item in bestRows)
        {
            var r = item!.AsObject();
            string sel = r["model_id"]!.GetValue<string>();
            bool ready = readiness[sel]!["ready"]!.GetValue<bool>();
            int sp = r["strict_passes"]!.GetValue<int>();
            int st = r["short_tasks"]!.GetValue<int>();
            int cp = capstones[sel]!["passes"]!.GetValue<int>();
            int ct = capstones[sel]!["tasks"]!.GetValue<int>();
            double cost = r["cost_usd"]!.GetValue<double>();
            int calls = r["tool_calls"]!.GetValue<int>();
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture, $"| {sel} | {(ready ? "yes" : "no")} | {sp}/{st} | {cp}/{ct} | {cost:F4} | {calls} |"));
        }
        return sb.ToString();
    }

    private static string RenderCsv(JsonArray bestRows, JsonObject capstones, JsonObject readiness)
    {
        var sb = new StringBuilder();
        sb.AppendLine("model,effort,completed,short_tasks,strict_passes,summed_task_seconds,median_task_seconds,cost_usd,tool_calls,capstone_tasks,capstone_passes,ready");
        foreach (var item in bestRows)
        {
            var r = item!.AsObject();
            string sel = r["model_id"]!.GetValue<string>();
            string effort = r["effort"]!.GetValue<string>();
            int completed = r["completed"]!.GetValue<int>();
            int st = r["short_tasks"]!.GetValue<int>();
            int sp = r["strict_passes"]!.GetValue<int>();
            double sumS = r["summed_task_seconds"]!.GetValue<double>();
            double? medS = r["median_task_seconds"]?.GetValue<double?>();
            double cost = r["cost_usd"]!.GetValue<double>();
            int calls = r["tool_calls"]!.GetValue<int>();
            int ct = capstones[sel]!["tasks"]!.GetValue<int>();
            int cp = capstones[sel]!["passes"]!.GetValue<int>();
            bool ready = readiness[sel]!["ready"]!.GetValue<bool>();
            string medStr = medS is double mv ? mv.ToString("F3", CultureInfo.InvariantCulture) : "";
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{sel},{effort},{completed},{st},{sp},{sumS:F3},{medStr},{cost:F6},{calls},{ct},{cp},{ready}"));
        }
        return sb.ToString();
    }
}
