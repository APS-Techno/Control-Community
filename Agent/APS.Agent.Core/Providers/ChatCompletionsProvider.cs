using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace APS.Agent.Core.Providers;

public sealed partial class ChatCompletionsProvider(ProviderConfig config, HttpClient? httpClient = null) : IModelProvider
{
    private readonly HttpClient _client = ProviderHttpHelper.EnsureClient(config, httpClient);
    private readonly Uri _endpoint = ProviderHttpHelper.AppendPath(config.BaseUri, "chat/completions");

    [GeneratedRegex(@"<think>(.*?)</think>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex ThinkRegex();

    public async Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default)
    {
        var messages = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt,
            },
        };

        foreach (var msg in request.Messages)
        {
            if (msg.Role == "tool_result")
            {
                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = msg.ToolCallId ?? "",
                    ["content"] = msg.Text ?? "",
                });
                continue;
            }

            var obj = new JsonObject { ["role"] = msg.Role };
            if (msg.Role == "assistant" && !string.IsNullOrEmpty(msg.ReasoningText))
                obj["reasoning_content"] = msg.ReasoningText;
            if (msg.Text is not null || msg.ToolCalls is not { Count: > 0 })
                obj["content"] = msg.Text ?? "";

            if (msg.ToolCalls is { Count: > 0 })
            {
                var tcArr = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    tcArr.Add(new JsonObject
                    {
                        ["id"] = tc.CallId,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = tc.ArgumentsJson,
                        },
                    });
                }
                obj["tool_calls"] = tcArr;
            }
            messages.Add(obj);
        }

        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["messages"] = messages,
            ["max_tokens"] = config.MaxOutputTokens,
        };

        if (request.Tools.Count > 0)
        {
            var toolsArr = new JsonArray();
            foreach (var t in request.Tools)
            {
                toolsArr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = t.Name,
                        ["description"] = t.Description,
                        ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText()),
                    },
                });
            }
            body["tools"] = toolsArr;
        }

        if (config.MaxReasoningTokens is int budget && budget > 0)
            body["reasoning_budget_tokens"] = budget;

        string? apiKey = EndpointGuard.ReadApiKey(config.ApiKeyEnvVar);
        var (doc, snapshot) = await ProviderHttpHelper.PostJsonAsync(
            _client,
            _endpoint,
            body,
            apiKey is null ? null : h => h.Authorization = new AuthenticationHeaderValue("Bearer", apiKey),
            ct).ConfigureAwait(false);

        using (doc)
        {
            var root = doc.RootElement;
            string? responseId = root.TryGetProperty("id", out var idP) && idP.ValueKind == JsonValueKind.String ? idP.GetString() : null;
            var choice = root.GetProperty("choices")[0];
            string stopReason = choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String
                ? fr.GetString() ?? "stop"
                : "stop";
            var msgEl = choice.GetProperty("message");

            string rawContent = msgEl.TryGetProperty("content", out var cp) && cp.ValueKind == JsonValueKind.String
                ? cp.GetString() ?? ""
                : "";
            string? reasoningField = msgEl.TryGetProperty("reasoning_content", out var rp) && rp.ValueKind == JsonValueKind.String
                ? rp.GetString()
                : null;

            var (cleanText, inlineThink) = SplitThinkTags(rawContent);
            string? combinedReasoning = CombineReasoning(reasoningField, inlineThink);

            var toolCalls = new List<ProviderToolCall>();
            if (msgEl.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in tcs.EnumerateArray())
                {
                    string callId = tc.TryGetProperty("id", out var cid) && cid.ValueKind == JsonValueKind.String ? cid.GetString() ?? "" : "";
                    var fn = tc.GetProperty("function");
                    string name = fn.GetProperty("name").GetString() ?? "";
                    string args = fn.TryGetProperty("arguments", out var ap)
                        ? (ap.ValueKind == JsonValueKind.String ? ap.GetString() ?? "{}" : ap.GetRawText())
                        : "{}";
                    toolCalls.Add(new ProviderToolCall(callId, name, args));
                }
            }

            var usage = ReadUsage(root);
            return new ProviderTurnResponse(cleanText, combinedReasoning, [], toolCalls, usage, stopReason, responseId, snapshot);
        }
    }

    public static (string CleanText, string? ThinkText) SplitThinkTags(string content)
    {
        if (string.IsNullOrEmpty(content)) return ("", null);
        var matches = ThinkRegex().Matches(content);
        if (matches.Count == 0) return (content, null);
        var thinkParts = matches.Select(m => m.Groups[1].Value.Trim()).Where(s => s.Length > 0).ToList();
        string cleaned = ThinkRegex().Replace(content, "").Trim();
        return (cleaned, thinkParts.Count > 0 ? string.Join("\n", thinkParts) : null);
    }

    private static string? CombineReasoning(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a)) return string.IsNullOrWhiteSpace(b) ? null : b;
        if (string.IsNullOrWhiteSpace(b)) return a;
        return $"{a}\n{b}";
    }

    private static ProviderUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return new ProviderUsage(0, 0, 0);
        int input = u.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt32(out int pv) ? pv : 0;
        int output = u.TryGetProperty("completion_tokens", out var c) && c.TryGetInt32(out int cv) ? cv : 0;
        int total = u.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out int tv) ? tv : input + output;
        return new ProviderUsage(input, output, total);
    }
}
