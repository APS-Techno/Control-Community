using System.Text.Json;

namespace APS.SystemControl.Core.Remote.Agent;

public sealed record AgentRequest
{
    public int V { get; init; } = 1;
    public int Id { get; init; }
    public string Cmd { get; init; } = "";
    public JsonElement? Args { get; init; }
    /// <summary>Optional sender name behind a shared key (MQTT bridge `by`). Audit only — never
    /// an authorisation input. Sanitised by <see cref="AgentProtocol.SanitizeActor"/>.</summary>
    public string? Actor { get; init; }
}

public static class AgentErrors
{
    public const string AuthFailed = "auth_failed";
    public const string TierDenied = "tier_denied";
    public const string UnknownCommand = "unknown_command";
    public const string UnknownDevice = "unknown_device";
    public const string RecipeParseError = "recipe_parse_error";
    public const string Busy = "busy";
    public const string NotFound = "not_found";
    public const string AlreadyExists = "already_exists";
    public const string BadRequest = "bad_request";
    public const string Internal = "internal_error";
    public const string FeatureNotLicensed = "feature_not_licensed";
    public const string Unavailable = "unavailable";
    /// <summary>Mutating command while Control is not in Online-Remote (spec 2026-09-06 §3).</summary>
    public const string ControlMode = "control_mode";
}

public static class AgentProtocol
{
    public const int Version = 1;

    public static readonly JsonSerializerOptions Opt = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static bool TryParseRequest(string line, out AgentRequest req)
    {
        req = new AgentRequest();
        if (string.IsNullOrWhiteSpace(line)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<AgentRequest>(line, Opt);
            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Cmd)) return false;
            req = parsed with { Actor = SanitizeActor(parsed.Actor) };
            return true;
        }
        catch (JsonException) { return false; }
    }

    /// <summary>Trim, drop control characters, cap at 64 chars; empty → null. Free text from a
    /// broker ends up in log lines, so it is never allowed to carry a newline or exceed one line.</summary>
    internal static string? SanitizeActor(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return null;
        var sb = new System.Text.StringBuilder(Math.Min(actor.Length, 64));
        foreach (char c in actor.Trim())
        {
            if (char.IsControl(c)) continue;
            sb.Append(c);
            if (sb.Length == 64) break;
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    public static string FormatOk(int id, object? data) =>
        JsonSerializer.Serialize(new { id, ok = true, data }, Opt);

    public static string FormatError(int id, string code, string message, object? details = null) =>
        JsonSerializer.Serialize(new { id, ok = false, error = new { code, message, details } }, Opt);
}
