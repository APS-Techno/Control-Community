using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Serialization;

namespace APS.Agent.Core.Providers;

public enum ProviderProtocol
{
    ChatCompletions,
    OpenAiResponses,
    Anthropic,
}

public sealed record ProviderConfig(
    ProviderProtocol Protocol,
    Uri BaseUri,
    string ModelId,
    string? ApiKeyEnvVar = null,
    bool AllowExternalEndpoints = false,
    int? MaxReasoningTokens = null,
    int MaxOutputTokens = 8192,
    string? ReasoningEffort = null);

public sealed record ProviderToolCall(string CallId, string Name, string ArgumentsJson);

public sealed record ReasoningBlock(
    string Type,
    string? Text = null,
    string? Signature = null,
    string? Data = null,
    string? RawItemJson = null);

public sealed record ConversationMessage(
    string Role,
    string? Text,
    string? ReasoningText = null,
    IReadOnlyList<ReasoningBlock>? ReasoningBlocks = null,
    IReadOnlyList<ProviderToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null,
    bool IsError = false,
    bool Pinned = false,
    string? WakeReason = null,
    int? StepNumber = null);

public sealed record ProviderTurnRequest(
    string SystemPrompt,
    IReadOnlyList<ConversationMessage> Messages,
    IReadOnlyList<ToolDeclaration> Tools);

public sealed record ProviderUsage(
    int InputTokens,
    int OutputTokens,
    int TotalTokens,
    double? EstimatedCostUsd = null);

public sealed record ProviderTurnResponse(
    string AssistantText,
    string? ReasoningText,
    IReadOnlyList<ReasoningBlock> ReasoningBlocks,
    IReadOnlyList<ProviderToolCall> ToolCalls,
    ProviderUsage Usage,
    string StopReason,
    string? ResponseId = null,
    string? RedactedRequestSnapshotJson = null);

public interface IModelProvider
{
    Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default);
}

public static class ModelProviderFactory
{
    public static IModelProvider Create(ProviderConfig config, HttpClient? httpClient = null) => config.Protocol switch
    {
        ProviderProtocol.ChatCompletions => new ChatCompletionsProvider(config, httpClient),
        ProviderProtocol.OpenAiResponses => new OpenAiResponsesProvider(config, httpClient),
        ProviderProtocol.Anthropic => new AnthropicMessagesProvider(config, httpClient),
        _ => throw new ArgumentOutOfRangeException(nameof(config.Protocol), config.Protocol, null),
    };
}

internal static class ProviderHttpHelper
{
    public static Uri AppendPath(Uri baseUri, string relativePath)
    {
        string prefix = baseUri.AbsoluteUri.TrimEnd('/');
        string suffix = relativePath.TrimStart('/');
        return new Uri($"{prefix}/{suffix}", UriKind.Absolute);
    }

    public static HttpClient EnsureClient(ProviderConfig config, HttpClient? supplied)
    {
        EndpointGuard.Validate(config.BaseUri, config.AllowExternalEndpoints);
        return supplied ?? new HttpClient(EndpointGuard.CreateNoRedirectHandler(), disposeHandler: true);
    }

    public static async Task<(JsonDocument Doc, string RedactedSnapshot)> PostJsonAsync(
        HttpClient client,
        Uri targetUri,
        JsonObject body,
        Action<HttpRequestHeaders>? configureHeaders,
        CancellationToken ct)
    {
        string bodyJson = body.ToJsonString();
        string redactedSnapshot = new JsonObject
        {
            ["uri"] = targetUri.ToString(),
            ["body"] = JsonNode.Parse(bodyJson),
        }.ToJsonString();

        using var req = new HttpRequestMessage(HttpMethod.Post, targetUri)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json"),
        };
        configureHeaders?.Invoke(req.Headers);

        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        string raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Provider HTTP {(int)resp.StatusCode} from {targetUri.Host}: {raw}");

        return (StrictJson.Parse(raw), redactedSnapshot);
    }
}
