namespace APS.SystemControl.Core.Remote.Agent;

internal sealed class AgentOpException(string code, string message, object? details = null)
    : Exception(message)
{
    /// <summary>Error code (e.g. "busy", "not_found") for wire-protocol error response.</summary>
    public string Code { get; } = code;
    public object? Details { get; } = details;
}
