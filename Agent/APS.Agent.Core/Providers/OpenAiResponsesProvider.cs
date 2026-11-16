using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace APS.Agent.Core.Providers;

public sealed class OpenAiResponsesProvider(ProviderConfig config, HttpClient? httpClient = null) : IModelProvider
{
    private readonly HttpClient _client = ProviderHttpHelper.EnsureClient(config, httpClient);
    private readonly Uri _endpoint = ProviderHttpHelper.AppendPath(config.BaseUri, "responses");

    public async Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default)
    {
        var input = new JsonArray();
        foreach (var msg in request.Messages)
        {
            if (msg.Role == "tool_result")
            {
                input.Add(new JsonObject
                {
                    ["type"] = "function_call_output",
                    ["call_id"] = msg.ToolCallId ?? "",
                    ["output"] = msg.Text ?? "",
                });
                continue;
            }

            if (msg.Role == "assistant")
            {
                if (msg.ReasoningBlocks is { Count: > 0 })
                {
                    foreach (var rb in msg.ReasoningBlocks)
                    {
                        if (!string.IsNullOrWhiteSpace(rb.RawItemJson))
                            input.Add(JsonNode.Parse(rb.RawItemJson));
                    }
                }
                if (!string.IsNullOrEmpty(msg.Text))
                {
                    input.Add(new JsonObject
                    {
                        ["type"] = "message",
                        ["role"] = "assistant",
                        ["content"] = msg.Text,
                    });
                }
                if (msg.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        input.Add(new JsonObject
                        {
                            ["type"] = "function_call",
                            ["call_id"] = tc.CallId,
                            ["name"] = tc.Name,
                            ["arguments"] = tc.ArgumentsJson,
                        });
                    }
                }
                continue;
            }

            input.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Text ?? "",
            });
        }

        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["instructions"] = request.SystemPrompt,
            ["input"] = input,
            ["max_output_tokens"] = config.MaxOutputTokens,
        };

        if (!string.IsNullOrWhiteSpace(config.ReasoningEffort))
            body["reasoning"] = new JsonObject { ["effort"] = config.ReasoningEffort };

        if (request.Tools.Count > 0)
        {
            var toolsArr = new JsonArray();
            foreach (var t in request.Tools)
            {
                toolsArr.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.Parameters.GetRawText()),
                });
            }
            body["tools"] = toolsArr;
        }

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
            string status = root.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String ? st.GetString() ?? "completed" : "completed";

            var texts = new List<string>();
            var reasoningBlocks = new List<ReasoningBlock>();
            var toolCalls = new List<ProviderToolCall>();

            if (root.TryGetProperty("output", out var outArr) && outArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in outArr.EnumerateArray())
                {
                    string itemType = item.TryGetProperty("type", out var tp) && tp.ValueKind == JsonValueKind.String ? tp.GetString() ?? "" : "";
                    if (itemType == "reasoning")
                    {
                        reasoningBlocks.Add(new ReasoningBlock("reasoning", RawItemJson: item.GetRawText()));
                    }
                    else if (itemType == "function_call")
                    {
                        string callId = item.TryGetProperty("call_id", out var cid) ? cid.GetString() ?? "" : "";
                        string name = item.TryGetProperty("name", out var nm) ? nm.GetString() ?? "" : "";
                        string args = item.TryGetProperty("arguments", out var ap)
                            ? (ap.ValueKind == JsonValueKind.String ? ap.GetString() ?? "{}" : ap.GetRawText())
                            : "{}";
                        toolCalls.Add(new ProviderToolCall(callId, name, args));
                    }
                    else if (itemType == "message" && item.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in contentArr.EnumerateArray())
                        {
                            if (c.TryGetProperty("text", out var tx) && tx.ValueKind == JsonValueKind.String)
                                texts.Add(tx.GetString() ?? "");
                        }
                    }
                }
            }

            var usage = ReadUsage(root);
            return new ProviderTurnResponse(string.Join("\n", texts), null, reasoningBlocks, toolCalls, usage, status, responseId, snapshot);
        }
    }

    private static ProviderUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return new ProviderUsage(0, 0, 0);
        int input = u.TryGetProperty("input_tokens", out var p) && p.TryGetInt32(out int pv) ? pv : 0;
        int output = u.TryGetProperty("output_tokens", out var c) && c.TryGetInt32(out int cv) ? cv : 0;
        int total = u.TryGetProperty("total_tokens", out var t) && t.TryGetInt32(out int tv) ? tv : input + output;
        return new ProviderUsage(input, output, total);
    }
}
