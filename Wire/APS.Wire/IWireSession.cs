using System.Text.Json;
using System.Threading.Channels;

namespace APS.Wire;

/// <summary>A long-lived Agent-wire connection that can hold a telemetry-bus subscription: ordinary
/// request/response calls plus an open-ended stream of unsolicited <c>push</c> frames.</summary>
public interface IWireSession : IAsyncDisposable
{
    /// <summary><c>data</c> of the auth reply: <c>tier</c>, <c>capabilities</c>, <c>machine</c>.</summary>
    JsonElement AuthData { get; }
    bool HasCapability(string name);
    /// <param name="actor">Optional human/sender name for Control's audit line (`[key/actor] cmd …`).
    /// Never an authorisation input. Omitted from the request line when null.</param>
    Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default, string? actor = null);
    /// <summary>Every line carrying a <c>push</c> key, in wire order. <see cref="ChannelReader{T}.Completion"/>
    /// faults with the <see cref="IOException"/> that ended the connection, or completes cleanly on dispose.</summary>
    ChannelReader<JsonElement> Pushes { get; }
}
