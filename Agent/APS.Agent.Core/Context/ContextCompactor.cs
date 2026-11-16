using System.Text.Json;
using APS.Agent.Core.Providers;

namespace APS.Agent.Core.Context;

public static class ContextCompactor
{
    private static readonly HashSet<string> PinnedToolNames = new(StringComparer.Ordinal)
    {
        "describe_machine",
        "describe_hardware",
        "describe_recipes",
        "describe_dsl",
        "describe_interlocks",
        "describe_alarms",
        "compute_deposition_plan",
    };

    private static readonly HashSet<string> CriticalWakeReasons = new(StringComparer.OrdinalIgnoreCase)
    {
        "fault_or_alarm",
        "paused",
        "aborted",
        "completed",
    };

    private sealed class TurnUnit
    {
        public required List<ConversationMessage> Messages { get; init; }
        public bool IsPinned { get; set; }
        public string? WatchWakeReason { get; init; }
        public bool IsLatestValidationOrUpload { get; set; }
        public int EstimatedTokens => Messages.Sum(EstimateMessageTokens);
    }

    public static int ComputeBudget(int configuredBudget, int? strataNCtx, int outputReserve = 8192)
    {
        if (strataNCtx is int nCtx && nCtx > 0)
            return Math.Min(configuredBudget, Math.Max(4096, nCtx - 8192));
        return configuredBudget > outputReserve
            ? Math.Max(4096, configuredBudget - outputReserve)
            : configuredBudget;
    }

    public static int EstimateTokens(
        string systemPrompt,
        IReadOnlyList<ConversationMessage> messages,
        int? lastPromptTokens = null,
        int lastPromptMessageCount = 0)
    {
        if (lastPromptTokens is int pt && lastPromptMessageCount >= 0 && lastPromptMessageCount <= messages.Count)
        {
            int delta = messages.Skip(lastPromptMessageCount).Sum(EstimateMessageTokens);
            return pt + delta;
        }
        int sysTokens = Math.Max(1, (systemPrompt ?? "").Length / 3);
        return sysTokens + messages.Sum(EstimateMessageTokens);
    }

    public static IReadOnlyList<ConversationMessage> PrepareHistory(
        ProviderProtocol protocol,
        IReadOnlyList<ConversationMessage> history,
        int budgetTokens,
        int? lastPromptTokens = null,
        int lastPromptMessageCount = 0,
        string systemPrompt = "",
        bool stripHistoricalThinking = true)
    {
        var units = GroupIntoUnits(history);
        if (units.Count == 0) return [];

        if (protocol == ProviderProtocol.ChatCompletions && stripHistoricalThinking)
        {
            for (int i = 0; i < units.Count - 1; i++)
            {
                for (int j = 0; j < units[i].Messages.Count; j++)
                {
                    var m = units[i].Messages[j];
                    if (m.Role == "assistant" && (!string.IsNullOrEmpty(m.ReasoningText) || (m.Text ?? "").Contains("<think>", StringComparison.Ordinal)))
                    {
                        var (clean, _) = ChatCompletionsProvider.SplitThinkTags(m.Text ?? "");
                        units[i].Messages[j] = m with { Text = clean, ReasoningText = null };
                    }
                }
            }
        }

        MarkPins(units);

        int threshold = (int)(budgetTokens * 0.75);
        int currentTokens = EstimateCurrent(systemPrompt, units, lastPromptTokens, lastPromptMessageCount, history.Count);
        if (currentTokens <= threshold)
            return Flatten(units);

        var remaining = new List<TurnUnit>();
        foreach (var u in units)
        {
            if (currentTokens > threshold && !u.IsPinned && u.WatchWakeReason is "nominal_timeout" or "step_changed")
            {
                currentTokens -= u.EstimatedTokens;
                continue;
            }
            remaining.Add(u);
        }

        if (currentTokens <= threshold)
            return Flatten(remaining);

        var summarized = new List<TurnUnit>();
        var milestoneUnits = new List<TurnUnit>();
        foreach (var u in remaining)
        {
            if (!u.IsPinned && u.WatchWakeReason == "deposit_milestone")
            {
                milestoneUnits.Add(u);
                continue;
            }
            if (milestoneUnits.Count > 0 && currentTokens > threshold)
            {
                var summaryUnit = BuildMilestoneSummary(milestoneUnits);
                int saved = milestoneUnits.Sum(m => m.EstimatedTokens) - summaryUnit.EstimatedTokens;
                currentTokens -= saved;
                summarized.Add(summaryUnit);
                milestoneUnits.Clear();
            }
            else if (milestoneUnits.Count > 0)
            {
                summarized.AddRange(milestoneUnits);
                milestoneUnits.Clear();
            }
            summarized.Add(u);
        }
        if (milestoneUnits.Count > 0)
        {
            if (currentTokens > threshold)
                summarized.Add(BuildMilestoneSummary(milestoneUnits));
            else
                summarized.AddRange(milestoneUnits);
        }

        return Flatten(summarized);
    }

    private static int EstimateCurrent(
        string systemPrompt,
        List<TurnUnit> units,
        int? lastPromptTokens,
        int lastPromptMessageCount,
        int originalCount)
    {
        int flattenedCount = units.Sum(u => u.Messages.Count);
        if (lastPromptTokens is int pt && flattenedCount == originalCount && lastPromptMessageCount <= originalCount)
            return pt + units.SelectMany(u => u.Messages).Skip(lastPromptMessageCount).Sum(EstimateMessageTokens);
        int sysTokens = Math.Max(1, (systemPrompt ?? "").Length / 3);
        return sysTokens + units.Sum(u => u.EstimatedTokens);
    }

    private static List<TurnUnit> GroupIntoUnits(IReadOnlyList<ConversationMessage> history)
    {
        var units = new List<TurnUnit>();
        int i = 0;
        while (i < history.Count)
        {
            var m = history[i];
            if (m.Role == "assistant" && m.ToolCalls is { Count: > 0 })
            {
                var pair = new List<ConversationMessage> { m };
                i++;
                while (i < history.Count && history[i].Role == "tool_result")
                {
                    pair.Add(history[i]);
                    i++;
                }
                string? wake = pair.Select(ExtractWakeReason).FirstOrDefault(w => w is not null);
                units.Add(new TurnUnit { Messages = pair, WatchWakeReason = wake });
                continue;
            }
            units.Add(new TurnUnit { Messages = [m], WatchWakeReason = ExtractWakeReason(m) });
            i++;
        }
        return units;
    }

    private static void MarkPins(List<TurnUnit> units)
    {
        if (units.Count > 0 && units[0].Messages[0].Role == "user")
            units[0].IsPinned = true;

        int latestValidateIdx = -1;
        int latestUploadIdx = -1;
        for (int i = 0; i < units.Count; i++)
        {
            var names = units[i].Messages
                .SelectMany(m => (m.ToolCalls ?? []).Select(tc => tc.Name).Concat(m.ToolName is null ? [] : new[] { m.ToolName }))
                .ToList();
            if (names.Any(PinnedToolNames.Contains))
                units[i].IsPinned = true;
            if (units[i].WatchWakeReason is { } w && CriticalWakeReasons.Contains(w))
                units[i].IsPinned = true;
            if (units[i].Messages.Any(m => m.Pinned))
                units[i].IsPinned = true;
            if (names.Contains("validate_recipe", StringComparer.Ordinal))
                latestValidateIdx = i;
            if (names.Contains("upload_recipe", StringComparer.Ordinal))
                latestUploadIdx = i;
        }
        if (latestValidateIdx >= 0) units[latestValidateIdx].IsPinned = true;
        if (latestUploadIdx >= 0) units[latestUploadIdx].IsPinned = true;

        int keepTailStart = Math.Max(0, units.Count - 4);
        for (int i = keepTailStart; i < units.Count; i++)
            units[i].IsPinned = true;
    }

    private static string? ExtractWakeReason(ConversationMessage msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.WakeReason))
            return msg.WakeReason;
        if (msg.Role != "tool_result" || msg.ToolName != "watch_recipe" || string.IsNullOrWhiteSpace(msg.Text))
            return null;
        try
        {
            using var doc = JsonDocument.Parse(msg.Text);
            if (doc.RootElement.TryGetProperty("wakeReason", out var wr) && wr.ValueKind == JsonValueKind.String)
                return wr.GetString();
        }
        catch
        {
        }
        return null;
    }

    private static TurnUnit BuildMilestoneSummary(List<TurnUnit> milestones)
    {
        int count = milestones.Count;
        return new TurnUnit
        {
            IsPinned = true,
            Messages =
            [
                new ConversationMessage(
                    "user",
                    $"[Compacted watch summary: {count} earlier deposit_milestone updates omitted to preserve context budget.]",
                    Pinned: true),
            ],
        };
    }

    private static int EstimateMessageTokens(ConversationMessage m)
    {
        int chars = (m.Text?.Length ?? 0) + (m.ReasoningText?.Length ?? 0);
        if (m.ToolCalls is { Count: > 0 })
            chars += m.ToolCalls.Sum(tc => tc.Name.Length + tc.ArgumentsJson.Length);
        return Math.Max(1, chars / 3);
    }

    private static IReadOnlyList<ConversationMessage> Flatten(List<TurnUnit> units) =>
        units.SelectMany(u => u.Messages).ToList();
}
