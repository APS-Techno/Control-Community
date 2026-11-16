using System.Globalization;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Providers;

namespace APS.Agent.Ui;

public sealed class AgentUiSettings
{
    public AgentMode InitialMode { get; set; } = AgentMode.Copilot;
    public ProviderProtocol Protocol { get; set; } = ProviderProtocol.ChatCompletions;
    public Uri Endpoint { get; set; } = new("http://127.0.0.1:8081/v1");
    public string ModelId { get; set; } = "";
    public string ApiKeyEnvVar { get; set; } = "";
    public bool AllowExternalEndpoints { get; set; }
    public string McpBin { get; set; } = "APS.Mcp.exe";
    public string McpConfigPath { get; set; } = "";
    public string SkillDir { get; set; } = "";
    public string SessionTier { get; set; } = "Read";
    public bool AllowDirectDeviceCommand { get; set; }
    public string ReasoningEffort { get; set; } = "";
    public int MaxTurns { get; set; } = 40;
    public int ContextBudgetTokens { get; set; } = 65536;
    public Uri? ElnEndpointUri { get; set; }
    public string ElnApiKeyEnvVar { get; set; } = "APS_ELN_API_KEY";
    public string? ElnMcpCommand { get; set; }
    public IReadOnlyList<string>? ElnMcpArgs { get; set; }
    public string ElnMcpToolName { get; set; } = "upload_run_bundle";
    public string ElnLocalExportDir { get; set; } = Path.Combine(Path.GetTempPath(), "aps-eln-exports");
    public bool ElnAllowExternal { get; set; }
    public string? StartupWarning { get; set; }

    public ElnConfig ToElnConfig() => new(
        ElnEndpointUri,
        string.IsNullOrWhiteSpace(ElnApiKeyEnvVar) ? null : ElnApiKeyEnvVar.Trim(),
        string.IsNullOrWhiteSpace(ElnLocalExportDir) ? Path.Combine(Path.GetTempPath(), "aps-eln-exports") : ElnLocalExportDir.Trim(),
        ElnAllowExternal,
        string.IsNullOrWhiteSpace(ElnMcpCommand) ? null : ElnMcpCommand.Trim(),
        ElnMcpArgs,
        string.IsNullOrWhiteSpace(ElnMcpToolName) ? "upload_run_bundle" : ElnMcpToolName.Trim());

    public static AgentUiSettings FromArgs(string[] args)
    {
        var opts = ParseArgs(args, out var flags);
        var warnings = new List<string>();

        string? epRaw = opts.GetValueOrDefault("--endpoint") ?? Environment.GetEnvironmentVariable("APS_AGENT_ENDPOINT");
        Uri endpoint = new("http://127.0.0.1:8081/v1");
        if (!string.IsNullOrWhiteSpace(epRaw))
        {
            if (TryParseHttpUri(epRaw, out var parsedEp))
                endpoint = parsedEp!;
            else
                warnings.Add($"Invalid LLM endpoint '{epRaw}'; using default {endpoint}.");
        }

        string? elnRaw = Environment.GetEnvironmentVariable("APS_ELN_ENDPOINT");
        Uri? elnEndpoint = null;
        if (!string.IsNullOrWhiteSpace(elnRaw))
        {
            if (TryParseHttpUri(elnRaw, out var parsedEln))
                elnEndpoint = parsedEln;
            else
                warnings.Add($"Invalid APS_ELN_ENDPOINT '{elnRaw}'; falling back to local bundle export.");
        }

        string? elnKeyEnv = Environment.GetEnvironmentVariable("APS_ELN_API_KEY_ENV") ?? "APS_ELN_API_KEY";
        string elnExportDir = Environment.GetEnvironmentVariable("APS_ELN_EXPORT_DIR")
            ?? Path.Combine(Path.GetTempPath(), "aps-eln-exports");
        bool elnAllowExternal = string.Equals(
            Environment.GetEnvironmentVariable("APS_ELN_ALLOW_EXTERNAL"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        string? elnMcpCommand = Environment.GetEnvironmentVariable("APS_ELN_MCP_COMMAND");
        string? rawElnMcpArgs = opts.GetValueOrDefault("--eln-mcp-args") ?? Environment.GetEnvironmentVariable("APS_ELN_MCP_ARGS");
        IReadOnlyList<string>? elnMcpArgs = SplitSpaceTokens(rawElnMcpArgs);
        string? elnMcpTool = Environment.GetEnvironmentVariable("APS_ELN_MCP_TOOL");

        string? mcpBin = opts.GetValueOrDefault("--mcp-bin") ?? Environment.GetEnvironmentVariable("APS_MCP_BIN");
        if (string.IsNullOrWhiteSpace(mcpBin))
        {
            string localMcp = Path.Combine(AppContext.BaseDirectory, "APS.Mcp.exe");
            mcpBin = File.Exists(localMcp) ? localMcp : "APS.Mcp.exe";
        }

        string? mcpCfg = opts.GetValueOrDefault("--mcp-config") ?? Environment.GetEnvironmentVariable("APS_MCP_CONFIG");
        if (string.IsNullOrWhiteSpace(mcpCfg))
        {
            string localCfg = Path.Combine(AppContext.BaseDirectory, "apsmcp.json");
            mcpCfg = File.Exists(localCfg) ? localCfg : "";
        }

        string? skillDir = opts.GetValueOrDefault("--skill-dir")
            ?? Environment.GetEnvironmentVariable("APS_SKILL_DIR")
            ?? Environment.GetEnvironmentVariable("ADS_BENCH_SKILL_DIR")
            ?? "";

        AgentMode mode = Enum.TryParse<AgentMode>(opts.GetValueOrDefault("--mode"), ignoreCase: true, out var parsedMode)
            && Enum.IsDefined(parsedMode)
            && parsedMode != AgentMode.Benchmark
            ? parsedMode
            : AgentMode.Copilot;

        ProviderProtocol protocol = ParseProtocol(opts.GetValueOrDefault("--protocol"));

        int maxTurns = int.TryParse(opts.GetValueOrDefault("--max-turns"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mt) && mt > 0
            ? mt
            : 40;
        int contextBudget = int.TryParse(opts.GetValueOrDefault("--context-budget"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cb) && cb > 0
            ? cb
            : 65536;

        return new AgentUiSettings
        {
            InitialMode = mode,
            Protocol = protocol,
            Endpoint = endpoint,
            ModelId = opts.GetValueOrDefault("--model") ?? "",
            ApiKeyEnvVar = opts.GetValueOrDefault("--api-key-env") ?? "",
            AllowExternalEndpoints = flags.Contains("--allow-external"),
            McpBin = mcpBin,
            McpConfigPath = mcpCfg,
            SkillDir = skillDir,
            SessionTier = opts.GetValueOrDefault("--tier") ?? "Read",
            AllowDirectDeviceCommand = flags.Contains("--allow-device-cmd"),
            ReasoningEffort = opts.GetValueOrDefault("--reasoning-effort") ?? "",
            MaxTurns = maxTurns,
            ContextBudgetTokens = contextBudget,
            ElnEndpointUri = elnEndpoint,
            ElnApiKeyEnvVar = elnKeyEnv,
            ElnMcpCommand = string.IsNullOrWhiteSpace(elnMcpCommand) ? null : elnMcpCommand.Trim(),
            ElnMcpArgs = elnMcpArgs,
            ElnMcpToolName = string.IsNullOrWhiteSpace(elnMcpTool) ? "upload_run_bundle" : elnMcpTool.Trim(),
            ElnLocalExportDir = elnExportDir,
            ElnAllowExternal = elnAllowExternal,
            StartupWarning = warnings.Count > 0 ? string.Join(" ", warnings) : null,
        };
    }

    internal static IReadOnlyList<string>? SplitSpaceTokens(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return tokens.Length == 0 ? null : tokens;
    }

    internal static bool TryParseHttpUri(string? raw, out Uri? uri)
    {
        if (Uri.TryCreate((raw ?? "").Trim(), UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
        {
            uri = parsed;
            return true;
        }
        uri = null;
        return false;
    }

    private static ProviderProtocol ParseProtocol(string? raw) => (raw ?? "").ToLowerInvariant() switch
    {
        "openai_responses" or "openairesponses" or "responses" => ProviderProtocol.OpenAiResponses,
        "anthropic" or "anthropic_messages" => ProviderProtocol.Anthropic,
        _ => ProviderProtocol.ChatCompletions,
    };

    private static Dictionary<string, string> ParseArgs(string[] args, out HashSet<string> flags)
    {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            string tok = args[i];
            if (!tok.StartsWith("--", StringComparison.Ordinal))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                kv[tok] = args[++i];
            else
                flags.Add(tok);
        }
        return kv;
    }
}
