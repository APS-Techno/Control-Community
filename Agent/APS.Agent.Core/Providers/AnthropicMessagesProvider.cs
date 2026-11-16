using System.Text.Json;
using System.Text.Json.Nodes;

namespace APS.Agent.Core.Providers;

public sealed class AnthropicMessagesProvider(ProviderConfig config, HttpClient? httpClient = null) : IModelProvider
{
    private readonly HttpClient _client = ProviderHttpHelper.EnsureClient(config, httpClient);
    private readonly Uri _endpoint = ProviderHttpHelper.AppendPath(config.BaseUri, "messages");

    public async Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default)
    {
        var messages = new JsonArray();
        int i = 0;
        while (i < request.Messages.Count)
        {
            var msg = request.Messages[i];
            if (msg.Role == "tool_result")
            {
                var contentArr = new JsonArray();
                while (i < request.Messages.Count && request.Messages[i].Role == "tool_result")
                {
                    var tr = request.Messages[i];
                    contentArr.Add(new JsonObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = tr.ToolCallId ?? "",
                        ["content"] = tr.Text ?? "",
                        ["is_error"] = tr.IsError,
                    });
                    i++;
                }
                messages.Add(new JsonObject { ["role"] = "user", ["content"] = contentArr });
                continue;
            }

            if (msg.Role == "assistant")
            {
                var contentArr = new JsonArray();
                if (msg.ReasoningBlocks is { Count: > 0 })
                {
                    foreach (var rb in msg.ReasoningBlocks)
                    {
                        if (rb.Type == "thinking")
                        {
                            contentArr.Add(new JsonObject
                            {
                                ["type"] = "thinking",
                                ["thinking"] = rb.Text ?? "",
                                ["signature"] = rb.Signature ?? "",
                            });
                        }
                        else if (rb.Type == "redacted_thinking")
                        {
                            contentArr.Add(new JsonObject
                            {
                                ["type"] = "redacted_thinking",
                                ["data"] = rb.Data ?? "",
                            });
                        }
                    }
                }
                if (!string.IsNullOrEmpty(msg.Text))
                {
                    contentArr.Add(new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = msg.Text,
                    });
                }
                if (msg.ToolCalls is { Count: > 0 })
                {
                    foreach (var tc in msg.ToolCalls)
                    {
                        contentArr.Add(new JsonObject
                        {
                            ["type"] = "tool_use",
                            ["id"] = tc.CallId,
                            ["name"] = tc.Name,
                            ["input"] = JsonNode.Parse(string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson),
                        });
                    }
                }
                messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = contentArr });
                i++;
                continue;
            }

            messages.Add(new JsonObject
            {
                ["role"] = msg.Role,
                ["content"] = msg.Text ?? "",
            });
            i++;
        }

        var body = new JsonObject
        {
            ["model"] = config.ModelId,
            ["system"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = request.SystemPrompt,
                    ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
                },
            },
            ["messages"] = messages,
            ["max_tokens"] = config.MaxOutputTokens,
        };

        if (config.MaxReasoningTokens is int budget && budget > 0)
        {
            body["thinking"] = new JsonObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = budget,
            };
        }

        if (request.Tools.Count > 0)
        {
            var toolsArr = new JsonArray();
            foreach (var t in request.Tools)
            {
                toolsArr.Add(new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["input_schema"] = JsonNode.Parse(t.Parameters.GetRawText()),
                });
            }
            body["tools"] = toolsArr;
        }

        string? apiKey = EndpointGuard.ReadApiKey(config.ApiKeyEnvVar);
        var (doc, snapshot) = await ProviderHttpHelper.PostJsonAsync(
            _client,
            _endpoint,
            body,
            h =>
            {
                if (apiKey is not null) h.Add("x-api-key", apiKey);
                h.Add("anthropic-version", "2023-06-01");
            },
            ct).ConfigureAwait(false);

        using (doc)
        {
            var root = doc.RootElement;
            string? responseId = root.TryGetProperty("id", out var idP) && idP.ValueKind == JsonValueKind.String ? idP.GetString() : null;
            string stopReason = root.TryGetProperty("stop_reason", out var sr) && sr.ValueKind == JsonValueKind.String ? sr.GetString() ?? "end_turn" : "end_turn";

            var texts = new List<string>();
            var reasoningBlocks = new List<ReasoningBlock>();
            var reasoningTexts = new List<string>();
            var toolCalls = new List<ProviderToolCall>();

            if (root.TryGetProperty("content", out var contentArr) && contentArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in contentArr.EnumerateArray())
                {
                    string blockType = block.TryGetProperty("type", out var bt) && bt.ValueKind == JsonValueKind.String ? bt.GetString() ?? "" : "";
                    if (blockType == "thinking")
                    {
                        string thinkText = block.TryGetProperty("thinking", out var th) ? th.GetString() ?? "" : "";
                        string sig = block.TryGetProperty("signature", out var sg) ? sg.GetString() ?? "" : "";
                        reasoningBlocks.Add(new ReasoningBlock("thinking", Text: thinkText, Signature: sig));
                        if (!string.IsNullOrWhiteSpace(thinkText)) reasoningTexts.Add(thinkText);
                    }
                    else if (blockType == "redacted_thinking")
                    {
                        string data = block.TryGetProperty("data", out var dt) ? dt.GetString() ?? "" : "";
                        reasoningBlocks.Add(new ReasoningBlock("redacted_thinking", Data: data));
                    }
                    else if (blockType == "text")
                    {
                        string tx = block.TryGetProperty("text", out var tp) ? tp.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(tx)) texts.Add(tx);
                    }
                    else if (blockType == "tool_use")
                    {
                        string id = block.TryGetProperty("id", out var ip) ? ip.GetString() ?? "" : "";
                        string name = block.TryGetProperty("name", out var np) ? np.GetString() ?? "" : "";
                        string inputJson = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                        toolCalls.Add(new ProviderToolCall(id, name, inputJson));
                    }
                }
            }

            var usage = ReadUsage(root);
            string? combinedReasoning = reasoningTexts.Count > 0 ? string.Join("\n", reasoningTexts) : null;
            return new ProviderTurnResponse(string.Join("\n", texts), combinedReasoning, reasoningBlocks, toolCalls, usage, stopReason, responseId, snapshot);
        }
    }

    private static ProviderUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
            return new ProviderUsage(0, 0, 0);
        int input = u.TryGetProperty("input_tokens", out var p) && p.TryGetInt32(out int pv) ? pv : 0;
        int output = u.TryGetProperty("output_tokens", out var c) && c.TryGetInt32(out int cv) ? cv : 0;
        return new ProviderUsage(input, output, input + output);
    }
}
