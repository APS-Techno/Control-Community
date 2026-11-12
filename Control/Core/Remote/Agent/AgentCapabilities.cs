using APS.SystemControl.Hosting;

namespace APS.SystemControl.Core.Remote.Agent;

internal static class AgentCapabilities
{
    public static IReadOnlyList<string> For(ControlEdition edition)
        => ["controlMode", .. edition.Find<IAgentStreamExtension>()?.Capabilities ?? []];
}
