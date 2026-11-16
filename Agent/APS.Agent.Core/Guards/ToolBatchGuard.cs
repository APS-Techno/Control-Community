namespace APS.Agent.Core.Guards;

public sealed record PendingToolCall(string CallId, string Name, string ArgumentsJson);

public sealed record BatchDecision(
    bool Allowed,
    bool AbortTrial,
    string? DenialCategory,
    string? ErrorMessage);

public static class ToolBatchGuard
{
    public static BatchDecision Evaluate(
        AgentMode mode,
        IReadOnlyList<PendingToolCall> calls,
        IReadOnlySet<string> allowedToolNames)
    {
        if (calls.Count == 0)
            return new BatchDecision(true, false, null, null);

        foreach (var call in calls)
        {
            if (mode == AgentMode.Copilot && ToolPolicy.IsMutating(call.Name))
            {
                return new BatchDecision(
                    false,
                    false,
                    "mutating_tool_blocked_in_copilot_mode",
                    $"Mutating tool '{call.Name}' is blocked in Copilot mode.");
            }
            if (!allowedToolNames.Contains(call.Name))
            {
                return new BatchDecision(
                    false,
                    mode == AgentMode.Benchmark,
                    "tool_outside_allowlist",
                    $"Tool '{call.Name}' is outside the active allowlist.");
            }
        }

        if (calls.Count > 1 && calls.Any(c => ToolPolicy.IsMutating(c.Name)))
        {
            return new BatchDecision(
                false,
                mode == AgentMode.Benchmark,
                "assistant_batch_denied",
                "Mutating tools must be called alone (single call per turn).");
        }

        return new BatchDecision(true, false, null, null);
    }
}
