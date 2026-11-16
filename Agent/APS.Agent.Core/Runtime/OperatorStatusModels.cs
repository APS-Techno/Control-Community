using System.Text.Json;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Serialization;
using APS.Agent.Core.Strata;

namespace APS.Agent.Core.Runtime;

public sealed record MachineIdentityBadge(
    bool Connected,
    bool IsEmulator,
    string Profile,
    string ExecutionTarget,
    string SessionTier,
    string AppVersion,
    string BadgeText,
    string Tooltip)
{
    public static MachineIdentityBadge Offline(string sessionTier = "Read", string? reason = null)
    {
        string tier = string.IsNullOrWhiteSpace(sessionTier) ? "Read" : sessionTier.Trim();
        return new MachineIdentityBadge(
            Connected: false,
            IsEmulator: false,
            Profile: "",
            ExecutionTarget: "unknown",
            SessionTier: tier,
            AppVersion: "",
            BadgeText: $"MCP OFFLINE | TIER: {tier.ToUpperInvariant()}",
            Tooltip: string.IsNullOrWhiteSpace(reason) ? "Not connected to APS Control MCP server." : reason);
    }

    public static MachineIdentityBadge FromDescribeMachineJson(string json, string fallbackTier = "Read")
    {
        using var doc = StrictJson.Parse(json);
        JsonElement root = doc.RootElement;

        string profile = ReadString(root, "profile") ?? "UNKNOWN";
        string appVersion = ReadString(root, "appVersion") ?? "";
        string executionTarget = ReadString(root, "executionTarget") ?? "";
        bool isEmulator = root.TryGetProperty("isEmulator", out var emuProp) && emuProp.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? emuProp.GetBoolean()
            : string.Equals(executionTarget, "emulator", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(executionTarget))
            executionTarget = isEmulator ? "emulator" : "physical";

        string tier = ReadString(root, "sessionTier") ?? fallbackTier;
        if (string.IsNullOrWhiteSpace(tier))
            tier = "Read";

        string targetTag = isEmulator ? "EMULATOR" : "HARDWARE";
        string profileTag = string.IsNullOrWhiteSpace(profile) ? "UNKNOWN" : profile;
        string badgeText = $"{targetTag} | {profileTag} | TIER: {tier.ToUpperInvariant()}";

        var tipParts = new List<string>
        {
            $"Target: {executionTarget}",
            $"Profile: {profileTag}",
            $"Session Tier: {tier}",
        };
        if (!string.IsNullOrWhiteSpace(appVersion))
            tipParts.Add($"Control Version: {appVersion}");

        return new MachineIdentityBadge(
            Connected: true,
            IsEmulator: isEmulator,
            Profile: profileTag,
            ExecutionTarget: executionTarget,
            SessionTier: tier,
            AppVersion: appVersion,
            BadgeText: badgeText,
            Tooltip: string.Join("\n", tipParts));
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}

public enum IndicatorState
{
    Off,
    Ready,
    Warning,
    Error,
}

public sealed record IndicatorStatus(IndicatorState State, string Label, string Detail);

public sealed record ConnectionIndicatorsSnapshot(
    IndicatorStatus Strata,
    IndicatorStatus CloudLlm,
    IndicatorStatus Eln3);

public static class AgentConnectionMonitor
{
    public static async Task<ConnectionIndicatorsSnapshot> EvaluateAsync(
        Uri llmEndpoint,
        bool allowExternalEndpoints,
        string? apiKeyEnvVar,
        ElnConfig elnConfig,
        HttpClient? httpClient = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(llmEndpoint);
        ArgumentNullException.ThrowIfNull(elnConfig);

        IndicatorStatus strata;
        IndicatorStatus cloud;

        if (!IsHttpOrHttps(llmEndpoint))
        {
            strata = new IndicatorStatus(IndicatorState.Off, "STRATA STANDBY", "Invalid endpoint scheme");
            cloud = new IndicatorStatus(IndicatorState.Error, "LLM INVALID", $"Unsupported URI scheme '{llmEndpoint.Scheme}'");
        }
        else if (EndpointGuard.IsLoopbackHost(llmEndpoint.Host))
        {
            cloud = new IndicatorStatus(IndicatorState.Off, "CLOUD STANDBY", "Local loopback endpoint active");
            try
            {
                StrataModelInfo info = await StrataSupervisor.ProbeRunningAsync(llmEndpoint, httpClient, ct).ConfigureAwait(false);
                strata = new IndicatorStatus(IndicatorState.Ready, "STRATA LOADED", $"{info.ModelId} (n_ctx={info.NCtx})");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                strata = new IndicatorStatus(IndicatorState.Error, "STRATA OFFLINE", ex.Message);
            }
        }
        else
        {
            strata = new IndicatorStatus(IndicatorState.Off, "STRATA STANDBY", "External endpoint selected");
            if (!allowExternalEndpoints)
            {
                cloud = new IndicatorStatus(IndicatorState.Error, "CLOUD LOCKED", $"External host '{llmEndpoint.Host}' blocked by air-gap lock");
            }
            else if (!string.IsNullOrWhiteSpace(apiKeyEnvVar) && string.IsNullOrWhiteSpace(EndpointGuard.ReadApiKey(apiKeyEnvVar)))
            {
                cloud = new IndicatorStatus(IndicatorState.Warning, "CLOUD NO KEY", $"Environment variable '{apiKeyEnvVar}' is unset");
            }
            else
            {
                cloud = new IndicatorStatus(IndicatorState.Ready, $"CLOUD {llmEndpoint.Host.ToUpperInvariant()}", llmEndpoint.ToString());
            }
        }

        IndicatorStatus eln;
        if (!string.IsNullOrWhiteSpace(elnConfig.McpCommand))
        {
            eln = new IndicatorStatus(IndicatorState.Ready, "ELN3 MCP", $"{elnConfig.McpCommand} ({elnConfig.McpToolName})");
        }
        else if (elnConfig.EndpointUri is not null)
        {
            if (!IsHttpOrHttps(elnConfig.EndpointUri))
                eln = new IndicatorStatus(IndicatorState.Error, "ELN3 INVALID", $"Unsupported URI scheme '{elnConfig.EndpointUri.Scheme}'");
            else if (!EndpointGuard.IsLoopbackHost(elnConfig.EndpointUri.Host) && !elnConfig.AllowExternalEndpoint)
                eln = new IndicatorStatus(IndicatorState.Error, "ELN3 LOCKED", $"External host '{elnConfig.EndpointUri.Host}' blocked by air-gap lock");
            else
                eln = new IndicatorStatus(IndicatorState.Ready, "ELN3 HTTP", elnConfig.EndpointUri.ToString());
        }
        else
        {
            eln = new IndicatorStatus(IndicatorState.Warning, "ELN3 LOCAL", $"Local bundle export: {elnConfig.LocalExportDir}");
        }

        return new ConnectionIndicatorsSnapshot(strata, cloud, eln);
    }

    private static bool IsHttpOrHttps(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
