using APS.Agent.Core.Schemas;

namespace APS.Agent.Core.Guards;

public enum AgentMode
{
    Copilot,
    Autonomous,
    Benchmark,
}

public static class ToolPolicy
{
    private static readonly HashSet<string> MutatingTools = new(StringComparer.Ordinal)
    {
        "upload_recipe",
        "start_recipe",
        "abort_recipe",
        "resolve_pause",
        "device_command",
    };

    public static bool IsMutating(string toolName) => MutatingTools.Contains(toolName);

    public static IReadOnlyList<ToolDeclaration> FilterDeclarations(
        AgentMode mode,
        IReadOnlyList<ToolDeclaration> allTools,
        string sessionTier,
        bool allowDirectDeviceCommand,
        IReadOnlySet<string>? benchmarkAllowedTools)
    {
        return mode switch
        {
            AgentMode.Copilot => allTools.Where(t => !IsMutating(t.Name)).ToList(),
            AgentMode.Autonomous => allTools.Where(t =>
                !IsMutating(t.Name)
                || t.Name != "device_command"
                || (allowDirectDeviceCommand && string.Equals(sessionTier, "Manual", StringComparison.OrdinalIgnoreCase))).ToList(),
            AgentMode.Benchmark => benchmarkAllowedTools is null
                ? allTools.ToList()
                : allTools.Where(t => benchmarkAllowedTools.Contains(t.Name)).ToList(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
        };
    }
}
