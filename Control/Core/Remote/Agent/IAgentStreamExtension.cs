namespace APS.SystemControl.Core.Remote.Agent;

internal interface IAgentStreamExtension
{
    IReadOnlyList<string> Capabilities { get; }

    IAgentStreamSession CreateSession(int samplerMs, Func<string, CancellationToken, Task> writeLine,
        CancellationToken connectionCt);
}

internal interface IAgentStreamSession : IDisposable
{
    bool Handles(string cmd);

    Task<string> HandleAsync(AgentRequest req, AgentTier tier, CancellationToken ct);
}
