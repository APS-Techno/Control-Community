using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;

namespace APS.Agent.Core.Eln;

public sealed record ElnConfig(
    Uri? EndpointUri,
    string? ApiKeyEnvVar,
    string LocalExportDir,
    bool AllowExternalEndpoint = false,
    string? McpCommand = null,
    IReadOnlyList<string>? McpArgs = null,
    string McpToolName = "upload_run_bundle")
{
    public static ElnConfig FromEnvironment(string? defaultExportDir = null)
    {
        string? rawUri = Environment.GetEnvironmentVariable("APS_ELN_ENDPOINT");
        Uri? uri = string.IsNullOrWhiteSpace(rawUri) ? null : new Uri(rawUri, UriKind.Absolute);
        string? keyEnv = Environment.GetEnvironmentVariable("APS_ELN_API_KEY_ENV") ?? "APS_ELN_API_KEY";
        string exportDir = Environment.GetEnvironmentVariable("APS_ELN_EXPORT_DIR")
            ?? defaultExportDir
            ?? Path.Combine(Path.GetTempPath(), "aps-eln-exports");
        bool allowExternal = string.Equals(
            Environment.GetEnvironmentVariable("APS_ELN_ALLOW_EXTERNAL"),
            "true",
            StringComparison.OrdinalIgnoreCase);
        string? mcpCommand = Environment.GetEnvironmentVariable("APS_ELN_MCP_COMMAND");
        string? rawMcpArgs = Environment.GetEnvironmentVariable("APS_ELN_MCP_ARGS");
        IReadOnlyList<string>? mcpArgs = string.IsNullOrWhiteSpace(rawMcpArgs)
            ? null
            : rawMcpArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? mcpTool = Environment.GetEnvironmentVariable("APS_ELN_MCP_TOOL");
        return new ElnConfig(
            uri,
            keyEnv,
            exportDir,
            allowExternal,
            string.IsNullOrWhiteSpace(mcpCommand) ? null : mcpCommand.Trim(),
            mcpArgs,
            string.IsNullOrWhiteSpace(mcpTool) ? "upload_run_bundle" : mcpTool.Trim());
    }
}

public sealed record ElnRunBundle(
    string RunId,
    string RecipeName,
    string Outcome,
    string? RecipePath,
    string? JournalPath,
    string? TelemetryTsvPath);

public sealed record ElnSyncResult(string Mode, bool Uploaded, string OutputLocation);

public sealed class ElnSyncClient(ElnConfig config, HttpClient? httpClient = null, IMcpToolInvoker? mcpInvoker = null)
{
    public async Task<ElnSyncResult> PublishRunAsync(ElnRunBundle bundle, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (mcpInvoker is not null || !string.IsNullOrWhiteSpace(config.McpCommand))
            return await PublishViaMcpAsync(bundle, ct).ConfigureAwait(false);

        if (config.EndpointUri is null)
            return ExportLocalBundle(bundle);

        EndpointGuard.Validate(config.EndpointUri, config.AllowExternalEndpoint);
        using var owned = httpClient is null
            ? new HttpClient(EndpointGuard.CreateNoRedirectHandler(), disposeHandler: true)
            : null;
        HttpClient client = httpClient ?? owned!;

        JsonObject payload = BuildPayload(bundle);

        using var req = new HttpRequestMessage(HttpMethod.Post, config.EndpointUri)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        string? apiKey = EndpointGuard.ReadApiKey(config.ApiKeyEnvVar);
        if (!string.IsNullOrWhiteSpace(apiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        string body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"ELN sync failed with HTTP {(int)resp.StatusCode}: {body}");

        return new ElnSyncResult("http", true, config.EndpointUri.ToString());
    }

    private async Task<ElnSyncResult> PublishViaMcpAsync(ElnRunBundle bundle, CancellationToken ct)
    {
        JsonObject payload = BuildPayload(bundle);
        using var doc = JsonDocument.Parse(payload.ToJsonString());

        if (mcpInvoker is not null)
        {
            var receipt = await mcpInvoker.CallAsync(config.McpToolName, doc.RootElement, ct).ConfigureAwait(false);
            if (receipt.IsError)
                throw new InvalidOperationException($"ELN MCP sync failed: {receipt.Text}");
            return new ElnSyncResult("mcp", true, config.McpCommand ?? config.McpToolName);
        }

        string cmd = config.McpCommand!;
        string? wd = Path.GetDirectoryName(cmd);
        await using var transient = await StdioMcpToolInvoker.ConnectAsync(
            cmd,
            config.McpArgs ?? [],
            workingDirectory: string.IsNullOrWhiteSpace(wd) ? null : wd,
            ct: ct).ConfigureAwait(false);
        var remoteReceipt = await transient.CallAsync(config.McpToolName, doc.RootElement, ct).ConfigureAwait(false);
        if (remoteReceipt.IsError)
            throw new InvalidOperationException($"ELN MCP sync failed: {remoteReceipt.Text}");
        return new ElnSyncResult("mcp", true, cmd);
    }

    private static JsonObject BuildPayload(ElnRunBundle bundle)
    {
        var payload = new JsonObject
        {
            ["runId"] = bundle.RunId,
            ["recipeName"] = bundle.RecipeName,
            ["outcome"] = bundle.Outcome,
            ["artifacts"] = new JsonArray(),
        };
        var arts = (JsonArray)payload["artifacts"]!;
        AddArtifactNode(arts, "recipe", bundle.RecipePath);
        AddArtifactNode(arts, "journal", bundle.JournalPath);
        AddArtifactNode(arts, "telemetryTsv", bundle.TelemetryTsvPath);
        return payload;
    }

    private ElnSyncResult ExportLocalBundle(ElnRunBundle bundle)
    {
        string outDir = Path.Combine(config.LocalExportDir, bundle.RunId);
        Directory.CreateDirectory(outDir);

        var copied = new List<(string FileName, string Sha256, long Bytes)>();
        foreach (string? src in new[] { bundle.RecipePath, bundle.JournalPath, bundle.TelemetryTsvPath })
        {
            if (string.IsNullOrWhiteSpace(src) || !File.Exists(src)) continue;
            string name = Path.GetFileName(src);
            string dst = Path.Combine(outDir, name);
            File.Copy(src, dst, overwrite: true);
            byte[] data = File.ReadAllBytes(dst);
            string sha = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
            copied.Add((name, sha, data.LongLength));
        }

        string metaJson = JsonSerializer.Serialize(new
        {
            runId = bundle.RunId,
            recipeName = bundle.RecipeName,
            outcome = bundle.Outcome,
            files = copied.Select(c => new { fileName = c.FileName, sha256 = c.Sha256, bytes = c.Bytes }),
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outDir, "bundle.json"), metaJson + "\n", new UTF8Encoding(false));

        var sums = copied.Select(c => $"{c.Sha256}  {c.FileName}");
        File.WriteAllText(Path.Combine(outDir, "SHA256SUMS.txt"), string.Join("\n", sums) + "\n", new UTF8Encoding(false));

        return new ElnSyncResult("local_bundle", false, outDir);
    }

    private static void AddArtifactNode(JsonArray arts, string kind, string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
        byte[] data = File.ReadAllBytes(path);
        arts.Add(new JsonObject
        {
            ["kind"] = kind,
            ["fileName"] = Path.GetFileName(path),
            ["sha256"] = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant(),
            ["contentBase64"] = Convert.ToBase64String(data),
        });
    }
}
