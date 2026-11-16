using System.Net;
using System.Text;
using System.Text.Json;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Schemas;

namespace APS.Agent.Tests;

public sealed class ProvidersTests
{
    private sealed class StubHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public readonly List<(Uri Uri, string Body, Dictionary<string, string> Headers)> Requests = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in request.Headers)
                headers[h.Key] = string.Join(",", h.Value);
            Requests.Add((request.RequestUri!, body, headers));
            return responder(request, body);
        }
    }

    private static List<ToolDeclaration> SampleTools() =>
    [
        new("describe_machine", "Describe machine", JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement),
    ];

    private static ProviderTurnRequest SampleRequest() => new(
        "System rules.",
        [new ConversationMessage("user", "Check machine")],
        SampleTools());

    [Fact]
    public void EndpointGuard_blocks_non_loopback_by_default_and_allows_loopback()
    {
        EndpointGuard.Validate(new Uri("http://127.0.0.1:8081/v1"), allowExternalEndpoints: false);
        EndpointGuard.Validate(new Uri("http://localhost:8081/v1"), allowExternalEndpoints: false);
        EndpointGuard.Validate(new Uri("http://[::1]:8081/v1"), allowExternalEndpoints: false);

        Assert.Throws<InvalidOperationException>(() =>
            EndpointGuard.Validate(new Uri("https://api.openai.com/v1"), allowExternalEndpoints: false));

        EndpointGuard.Validate(new Uri("https://api.openai.com/v1"), allowExternalEndpoints: true);
    }

    [Fact]
    public async Task Providers_disable_redirects_strip_api_keys_from_snapshots_and_reject_non_finite_json()
    {
        Environment.SetEnvironmentVariable("APS_TEST_API_KEY", "sk-secret-value-123");
        try
        {
            using var defaultHandler = EndpointGuard.CreateNoRedirectHandler();
            Assert.False(defaultHandler.AllowAutoRedirect);

            var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"r1","choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":10,"completion_tokens":2,"total_tokens":12}}""", Encoding.UTF8, "application/json"),
            });
            var cfg = new ProviderConfig(ProviderProtocol.ChatCompletions, new Uri("http://127.0.0.1:8081/v1"), "qwen3.8", ApiKeyEnvVar: "APS_TEST_API_KEY", MaxReasoningTokens: 1024);
            var provider = new ChatCompletionsProvider(cfg, new HttpClient(handler));

            var reply = await provider.CompleteAsync(SampleRequest());
            Assert.NotNull(reply.RedactedRequestSnapshotJson);
            Assert.DoesNotContain("sk-secret-value-123", reply.RedactedRequestSnapshotJson);

            var dupHandler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"1","id":"2","choices":[]}""", Encoding.UTF8, "application/json"),
            });
            var dupProvider = new ChatCompletionsProvider(cfg, new HttpClient(dupHandler));
            await Assert.ThrowsAnyAsync<JsonException>(() => dupProvider.CompleteAsync(SampleRequest()));
        }
        finally
        {
            Environment.SetEnvironmentVariable("APS_TEST_API_KEY", null);
        }
    }

    [Fact]
    public async Task ChatCompletionsProvider_serializes_reasoning_budget_tokens_and_parses_tool_calls()
    {
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "id": "chat-1",
                  "choices": [{
                    "finish_reason": "tool_calls",
                    "message": {
                      "role": "assistant",
                      "content": "<think>checking</think>Calling tool",
                      "reasoning_content": "step1",
                      "tool_calls": [{
                        "id": "tc_1",
                        "type": "function",
                        "function": { "name": "describe_machine", "arguments": "{}" }
                      }]
                    }
                  }],
                  "usage": { "prompt_tokens": 50, "completion_tokens": 12, "total_tokens": 62 }
                }
                """, Encoding.UTF8, "application/json"),
        });

        var cfg = new ProviderConfig(ProviderProtocol.ChatCompletions, new Uri("http://127.0.0.1:8081/v1"), "qwen3.8", MaxReasoningTokens: 2048);
        var provider = new ChatCompletionsProvider(cfg, new HttpClient(handler));
        var res = await provider.CompleteAsync(SampleRequest());

        Assert.Equal("http://127.0.0.1:8081/v1/chat/completions", handler.Requests[0].Uri.ToString());
        using var reqDoc = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(2048, reqDoc.RootElement.GetProperty("reasoning_budget_tokens").GetInt32());
        Assert.Equal("Calling tool", res.AssistantText);
        Assert.Contains("step1", res.ReasoningText);
        Assert.Single(res.ToolCalls);
        Assert.Equal("describe_machine", res.ToolCalls[0].Name);
        Assert.Equal(50, res.Usage.InputTokens);
    }

    [Fact]
    public async Task OpenAiResponsesProvider_serializes_responses_payload_and_preserves_reasoning_items()
    {
        Environment.SetEnvironmentVariable("APS_TEST_OPENAI_KEY", "sk-openai-xyz");
        try
        {
            var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id": "resp_1",
                      "status": "completed",
                      "output": [
                        { "type": "reasoning", "id": "rs_1", "encrypted_content": "enc_abc", "summary": [] },
                        { "type": "function_call", "call_id": "call_1", "name": "describe_machine", "arguments": "{}" }
                      ],
                      "usage": { "input_tokens": 30, "output_tokens": 15, "total_tokens": 45 }
                    }
                    """, Encoding.UTF8, "application/json"),
            });

            var cfg = new ProviderConfig(ProviderProtocol.OpenAiResponses, new Uri("https://api.openai.com/v1"), "gpt-5", ApiKeyEnvVar: "APS_TEST_OPENAI_KEY", AllowExternalEndpoints: true, ReasoningEffort: "high");
            var provider = new OpenAiResponsesProvider(cfg, new HttpClient(handler));
            var res = await provider.CompleteAsync(SampleRequest());

            Assert.Equal("https://api.openai.com/v1/responses", handler.Requests[0].Uri.ToString());
            Assert.Equal("Bearer sk-openai-xyz", handler.Requests[0].Headers["Authorization"]);
            Assert.Single(res.ReasoningBlocks);
            Assert.Equal("reasoning", res.ReasoningBlocks[0].Type);
            Assert.Contains("enc_abc", res.ReasoningBlocks[0].RawItemJson);
            Assert.Single(res.ToolCalls);
            Assert.Equal("call_1", res.ToolCalls[0].CallId);

            var followReq = new ProviderTurnRequest(
                "System rules.",
                [
                    new ConversationMessage("user", "Check machine"),
                    new ConversationMessage("assistant", "", ReasoningBlocks: res.ReasoningBlocks, ToolCalls: res.ToolCalls),
                    new ConversationMessage("tool_result", """{"profile":"ADS"}""", ToolCallId: "call_1", ToolName: "describe_machine"),
                ],
                SampleTools());
            await provider.CompleteAsync(followReq);
            Assert.Contains("enc_abc", handler.Requests[1].Body);
            Assert.Contains("function_call_output", handler.Requests[1].Body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APS_TEST_OPENAI_KEY", null);
        }
    }

    [Fact]
    public async Task AnthropicMessagesProvider_serializes_thinking_budget_and_preserves_signature_blocks()
    {
        Environment.SetEnvironmentVariable("APS_TEST_ANTHROPIC_KEY", "sk-ant-secret");
        try
        {
            var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id": "msg_1",
                      "stop_reason": "tool_use",
                      "content": [
                        { "type": "thinking", "thinking": "internal trace", "signature": "sig_999" },
                        { "type": "redacted_thinking", "data": "opaque_blob" },
                        { "type": "tool_use", "id": "tu_1", "name": "describe_machine", "input": {} }
                      ],
                      "usage": { "input_tokens": 40, "output_tokens": 20 }
                    }
                    """, Encoding.UTF8, "application/json"),
            });

            var cfg = new ProviderConfig(ProviderProtocol.Anthropic, new Uri("https://api.anthropic.com/v1"), "claude-sonnet", ApiKeyEnvVar: "APS_TEST_ANTHROPIC_KEY", AllowExternalEndpoints: true, MaxReasoningTokens: 4096);
            var provider = new AnthropicMessagesProvider(cfg, new HttpClient(handler));
            var res = await provider.CompleteAsync(SampleRequest());

            Assert.Equal("https://api.anthropic.com/v1/messages", handler.Requests[0].Uri.ToString());
            Assert.Equal("sk-ant-secret", handler.Requests[0].Headers["x-api-key"]);
            using var reqDoc = JsonDocument.Parse(handler.Requests[0].Body);
            Assert.Equal(4096, reqDoc.RootElement.GetProperty("thinking").GetProperty("budget_tokens").GetInt32());
            Assert.Equal(2, res.ReasoningBlocks.Count);
            Assert.Equal("sig_999", res.ReasoningBlocks[0].Signature);
            Assert.Equal("opaque_blob", res.ReasoningBlocks[1].Data);

            var followReq = new ProviderTurnRequest(
                "System rules.",
                [
                    new ConversationMessage("user", "Check machine"),
                    new ConversationMessage("assistant", "", ReasoningBlocks: res.ReasoningBlocks, ToolCalls: res.ToolCalls),
                    new ConversationMessage("tool_result", """{"profile":"ADS"}""", ToolCallId: "tu_1", ToolName: "describe_machine"),
                ],
                SampleTools());
            await provider.CompleteAsync(followReq);
            Assert.Contains("sig_999", handler.Requests[1].Body);
            Assert.Contains("opaque_blob", handler.Requests[1].Body);
            Assert.Contains("tool_result", handler.Requests[1].Body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APS_TEST_ANTHROPIC_KEY", null);
        }
    }

    [Fact]
    public async Task ChatAndResponsesProviders_default_missing_tool_call_arguments_to_empty_object()
    {
        var chatHandler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "id": "c_1",
                  "choices": [{
                    "finish_reason": "tool_calls",
                    "message": {
                      "role": "assistant",
                      "tool_calls": [{ "id": "call_1", "type": "function", "function": { "name": "describe_machine" } }]
                    }
                  }],
                  "usage": { "prompt_tokens": 5, "completion_tokens": 2, "total_tokens": 7 }
                }
                """, Encoding.UTF8, "application/json"),
        });
        var chat = new ChatCompletionsProvider(
            new ProviderConfig(ProviderProtocol.ChatCompletions, new Uri("http://127.0.0.1:8080/v1"), "m"),
            new HttpClient(chatHandler));
        var chatRes = await chat.CompleteAsync(SampleRequest());
        Assert.Equal("{}", Assert.Single(chatRes.ToolCalls).ArgumentsJson);

        var respHandler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "id": "r_1",
                  "status": "completed",
                  "output": [{ "type": "function_call", "call_id": "fc_1", "name": "describe_machine" }],
                  "usage": { "input_tokens": 5, "output_tokens": 2, "total_tokens": 7 }
                }
                """, Encoding.UTF8, "application/json"),
        });
        var resp = new OpenAiResponsesProvider(
            new ProviderConfig(ProviderProtocol.OpenAiResponses, new Uri("http://127.0.0.1:8080/v1"), "gpt-5"),
            new HttpClient(respHandler));
        var respRes = await resp.CompleteAsync(SampleRequest());
        Assert.Equal("{}", Assert.Single(respRes.ToolCalls).ArgumentsJson);
    }
}
