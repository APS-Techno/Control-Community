using System.Text.Json;

namespace APS.Mcp;

/// <param name="DownloadDir">Local folder that <c>fetch_run_files</c> writes run artifacts into
/// (one sub-folder per run id). Optional; the tool refuses until it is set. Point it inside the ELN
/// MCP server's <c>MCPUploadRoot</c> so the returned paths are accepted by <c>upload_file</c>.</param>
public sealed record McpBridgeConfig(string Host, int Port, string ApiKey, string? CertSha256, string? DownloadDir = null)
{
    public static McpBridgeConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Bridge config not found: {path}. Copy apsmcp.sample.json to apsmcp.json and fill in " +
                "host/port/apiKey (see README.md, section 'Bridge setup').", path);
        var cfg = JsonSerializer.Deserialize<McpBridgeConfig>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (cfg is null || string.IsNullOrWhiteSpace(cfg.Host) || string.IsNullOrWhiteSpace(cfg.ApiKey))
            throw new InvalidOperationException(
                $"Bridge config {path} must set host, port and apiKey (see README.md, section 'Bridge setup').");
        return cfg;
    }

    /// <summary>--config <path> arg, else APS_MCP_CONFIG env var, else apsmcp.json beside the exe.</summary>
    public static string ResolvePath(string[] args)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--config") return args[i + 1];
        return Environment.GetEnvironmentVariable("APS_MCP_CONFIG")
            ?? Path.Combine(AppContext.BaseDirectory, "apsmcp.json");
    }
}
