using System.Text;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Colour band for the status-strip MCP indicator. Tracks the authority an
/// agent currently holds, not link health — "nobody connected" and "feature off" are
/// the same band because neither can touch the machine.</summary>
internal enum AgentStatusLevel { Off, Read, Recipe, Manual }

/// <summary>One authenticated agent connection, as the indicator sees it.</summary>
internal readonly record struct AgentSession(string KeyName, AgentTier Tier);

/// <summary>What the two status-strip labels should show.</summary>
internal readonly record struct AgentStatusText(AgentStatusLevel Level, string Text, string Tooltip);

/// <summary>
/// Pure mapping from agent-server state to the status-strip indicator. Kept free of
/// WinForms so the display rules are unit-testable without a form.
/// </summary>
internal static class AgentStatusView
{
    internal static AgentStatusText Describe(
        bool listening,
        string? offReason,
        string endpoint,
        IReadOnlyList<AgentSession> sessions)
    {
        if (!listening)
            return new AgentStatusText(
                AgentStatusLevel.Off,
                "MCP OFF",
                "Agent MCP server is not running.\n"
                + (string.IsNullOrWhiteSpace(offReason) ? "Disabled." : offReason));

        // A None tier is a server-side bug or a torn read, never a grant. Drop such
        // entries rather than letting one light a colour band (fail closed).
        var live = sessions.Where(s => s.Tier != AgentTier.None).ToList();

        if (live.Count == 0)
            return new AgentStatusText(
                AgentStatusLevel.Off,
                "MCP IDLE",
                $"Agent MCP server listening on {endpoint}.\nNo agent connected.");

        var top = live.Max(s => s.Tier);
        var level = top switch
        {
            AgentTier.Manual => AgentStatusLevel.Manual,
            AgentTier.Recipe => AgentStatusLevel.Recipe,
            _                => AgentStatusLevel.Read,
        };

        string text = $"MCP {top.ToString().ToUpperInvariant()}";
        if (live.Count > 1) text += $" ({live.Count})";

        var tip = new StringBuilder($"Agent MCP server listening on {endpoint}.");
        tip.Append(live.Count == 1 ? "\n1 agent connected:" : $"\n{live.Count} agents connected:");
        foreach (var s in live)
            tip.Append($"\n  {s.KeyName} — {s.Tier.ToString().ToLowerInvariant()}");

        return new AgentStatusText(level, text, tip.ToString());
    }
}
