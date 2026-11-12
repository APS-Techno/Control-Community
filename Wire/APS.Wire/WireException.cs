using System.Text.Json;

namespace APS.Wire;

public sealed class WireException(string code, string message, Exception? inner = null)
    : Exception($"{code}: {message}", inner)
{
    public string Code { get; } = code;
}

public interface IWireCaller
{
    Task<JsonElement> CallAsync(string cmd, object? args, CancellationToken ct = default);
}

