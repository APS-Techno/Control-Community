using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using APS.Agent.Core.Context;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Skills;
using APS.Agent.Core.Strata;

namespace APS.Agent.Tests;

public sealed class ContextAndJournalTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aps-ctx-test-{Guid.NewGuid():N}");

    public ContextAndJournalTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

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

    private sealed class FakeMcpInvoker : IMcpToolInvoker
    {
        private readonly Func<string, JsonElement, ToolCallReceipt> _handler;
        public readonly List<string> ExecutedCalls = [];

        public FakeMcpInvoker(IReadOnlyList<ToolDeclaration> decls, Func<string, JsonElement, ToolCallReceipt> handler)
        {
            Declarations = decls;
            _handler = handler;
        }

        public IReadOnlyList<ToolDeclaration> Declarations { get; }

        public Task<ToolCallReceipt> CallAsync(
            string name,
            JsonElement arguments,
            CancellationToken ct,
            IProgress<WatchProgressNotice>? watchProgress = null)
        {
            ExecutedCalls.Add(name);
            return Task.FromResult(_handler(name, arguments));
        }
    }

    private sealed class ScriptedModelProvider(params ProviderTurnResponse[] responses) : IModelProvider
    {
        private readonly Queue<ProviderTurnResponse> _queue = new(responses);
        public readonly List<ProviderTurnRequest> Requests = [];

        public Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_queue.Dequeue());
        }
    }

    [Fact]
    public void ContextCompactor_strips_earlier_chat_thinking_while_keeping_anthropic_signatures_and_latest_turn()
    {
        var history = new List<ConversationMessage>
        {
            new("user", "Run check"),
            new("assistant", "Step 1", ReasoningText: "old internal reasoning", ToolCalls: [new ProviderToolCall("c1", "describe_machine", "{}")]),
            new("tool_result", """{"profile":"ADS"}""", ToolCallId: "c1", ToolName: "describe_machine"),
            new("assistant", "Step 2", ReasoningText: "latest reasoning", ToolCalls: [new ProviderToolCall("c2", "system_status", "{}")]),
            new("tool_result", """{"isRecipeRunning":false}""", ToolCallId: "c2", ToolName: "system_status"),
        };

        var chatPrepared = ContextCompactor.PrepareHistory(ProviderProtocol.ChatCompletions, history, budgetTokens: 32000, stripHistoricalThinking: true);
        Assert.Null(chatPrepared[1].ReasoningText);
        Assert.Equal("latest reasoning", chatPrepared[3].ReasoningText);

        var anthHistory = new List<ConversationMessage>
        {
            new("user", "Run check"),
            new("assistant", "Step 1", ReasoningBlocks: [new ReasoningBlock("thinking", "t1", Signature: "sig_1")], ToolCalls: [new ProviderToolCall("c1", "describe_machine", "{}")]),
            new("tool_result", """{"profile":"ADS"}""", ToolCallId: "c1", ToolName: "describe_machine"),
        };
        var anthPrepared = ContextCompactor.PrepareHistory(ProviderProtocol.Anthropic, anthHistory, budgetTokens: 32000, stripHistoricalThinking: true);
        Assert.Single(anthPrepared[1].ReasoningBlocks!);
        Assert.Equal("sig_1", anthPrepared[1].ReasoningBlocks![0].Signature);
    }

    [Fact]
    public void ContextCompactor_drops_nominal_timeout_and_step_changed_before_summarizing_deposit_milestones()
    {
        Assert.Equal(24576, ContextCompactor.ComputeBudget(65536, strataNCtx: 32768));

        var history = new List<ConversationMessage>
        {
            new("user", "Deposit 50 periods of Ru/C"),
            new("assistant", "", ToolCalls: [new ProviderToolCall("d1", "describe_machine", "{}")]),
            new("tool_result", """{"profile":"ADS"}""", ToolCallId: "d1", ToolName: "describe_machine"),
            new("assistant", "", ToolCalls: [new ProviderToolCall("p1", "compute_deposition_plan", "{}")]),
            new("tool_result", """{"period_A":68.5}""", ToolCallId: "p1", ToolName: "compute_deposition_plan"),
        };

        for (int i = 1; i <= 4; i++)
        {
            string cid = $"nt_{i}";
            history.Add(new("assistant", $"wait {i}", ToolCalls: [new ProviderToolCall(cid, "watch_recipe", "{}")]));
            history.Add(new("tool_result", $$"""{"wakeReason":"nominal_timeout","stepSequence":{{i}},"payload":"{{new string('x', 400)}}"}""", ToolCallId: cid, ToolName: "watch_recipe", WakeReason: "nominal_timeout"));
        }

        for (int i = 1; i <= 4; i++)
        {
            string cid = $"dm_{i}";
            history.Add(new("assistant", $"milestone {i}", ToolCalls: [new ProviderToolCall(cid, "watch_recipe", "{}")]));
            history.Add(new("tool_result", $$"""{"wakeReason":"deposit_milestone","executedDepositsCount":{{i * 10}},"payload":"{{new string('y', 400)}}"}""", ToolCallId: cid, ToolName: "watch_recipe", WakeReason: "deposit_milestone"));
        }

        history.Add(new("assistant", "fault check", ToolCalls: [new ProviderToolCall("fa_1", "watch_recipe", "{}")]));
        history.Add(new("tool_result", """{"wakeReason":"fault_or_alarm","stepSequence":99}""", ToolCallId: "fa_1", ToolName: "watch_recipe", WakeReason: "fault_or_alarm"));

        for (int i = 1; i <= 4; i++)
        {
            string cid = $"tail_{i}";
            history.Add(new("assistant", $"tail {i}", ToolCalls: [new ProviderToolCall(cid, "recipe_status", "{}")]));
            history.Add(new("tool_result", """{"isRecipeRunning":true}""", ToolCallId: cid, ToolName: "recipe_status"));
        }

        var compacted = ContextCompactor.PrepareHistory(ProviderProtocol.ChatCompletions, history, budgetTokens: 800, stripHistoricalThinking: true);

        Assert.Contains(compacted, m => m.ToolName == "describe_machine");
        Assert.Contains(compacted, m => m.ToolName == "compute_deposition_plan");
        Assert.Contains(compacted, m => m.WakeReason == "fault_or_alarm");
        Assert.DoesNotContain(compacted, m => m.WakeReason == "nominal_timeout");
        Assert.Contains(compacted, m => (m.Text ?? "").Contains("Compacted", StringComparison.OrdinalIgnoreCase));
        for (int i = 1; i <= 4; i++)
            Assert.Contains(compacted, m => m.ToolCallId == $"tail_{i}");

        var callIds = compacted.Where(m => m.ToolCalls is { Count: > 0 }).SelectMany(m => m.ToolCalls!).Select(tc => tc.CallId).ToHashSet();
        var resultIds = compacted.Where(m => m.Role == "tool_result").Select(m => m.ToolCallId!).ToHashSet();
        Assert.Equal(callIds, resultIds);
    }

    [Fact]
    public async Task StrataSupervisor_parses_v1_models_status_object_and_n_ctx()
    {
        var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "object": "list",
                  "data": [{
                    "id": "qwen3.8-flash-next-iq3_s",
                    "object": "model",
                    "status": { "value": "loaded" },
                    "meta": { "n_ctx": 524288 }
                  }]
                }
                """, Encoding.UTF8, "application/json"),
        });

        var info = await StrataSupervisor.ProbeRunningAsync(new Uri("http://127.0.0.1:8081/v1"), new HttpClient(handler));
        Assert.True(info.Loaded);
        Assert.Equal("qwen3.8-flash-next-iq3_s", info.ModelId);
        Assert.Equal(524288, info.NCtx);
        Assert.Equal("http://127.0.0.1:8081/v1/models", handler.Requests[0].Uri.ToString());

        StrataSupervisor.ValidateSingleGpuSelection(["--gpu", "0"], null);
        Assert.Throws<InvalidOperationException>(() => StrataSupervisor.ValidateSingleGpuSelection(["--gpu", "0,1"], null));
    }

    [Fact]
    public async Task AuditJournalWriter_writes_sha256_sidecar_and_preserves_full_reasoning_in_jsonl()
    {
        string journalPath = Path.Combine(_dir, "trial.jsonl");
        string sidecarPath = Path.Combine(_dir, "trial.json");
        await using (var writer = new AuditJournalWriter(journalPath))
        {
            await writer.WriteEventAsync("trial_start", new { ask = "Check", config_digest = "cfg_1", tool_schema_digest = "ts_1", skill_digest = "sk_1" });
            await writer.WriteEventAsync("assistant_turn", new { text = "Final", reasoning = "VERY_LONG_SECRET_REASONING" });
            await writer.WriteUsageAsync("u1", 0.0012, new ProviderUsage(10, 5, 15));
        }

        string sha = AuditJournalWriter.WriteSidecar(sidecarPath, new AuditSidecar(
            TrialId: "A01_local",
            Task: "A01",
            Selector: "local/qwen3.8",
            Thinking: "high",
            Status: "completed",
            Answer: "Final",
            DurationSeconds: 1.5,
            CatalogEstimatedCostUsd: 0.0012,
            ToolCallCount: 0,
            ConfigDigest: "cfg_1",
            ToolSchemaDigest: "ts_1",
            SkillDigest: "sk_1",
            JournalFile: "trial.jsonl",
            JournalSha256: AuditJournalWriter.ComputeFileSha256(journalPath),
            UsageRecords: [new UsageRecord("u1", 0.0012)]));

        string jsonlText = await File.ReadAllTextAsync(journalPath);
        Assert.Contains("VERY_LONG_SECRET_REASONING", jsonlText);
        string actualSha = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(journalPath))).ToLowerInvariant();
        Assert.Equal(actualSha, sha);
        using var sidecarDoc = JsonDocument.Parse(await File.ReadAllTextAsync(sidecarPath));
        Assert.Equal(actualSha, sidecarDoc.RootElement.GetProperty("journal_sha256").GetString());
    }

    [Fact]
    public async Task ElnSyncClient_posts_to_configured_endpoint_with_no_redirects_or_exports_local_bundle_when_unset()
    {
        string rcpx = Path.Combine(_dir, "run.rcpx");
        string journal = Path.Combine(_dir, "run.jsonl");
        string tsv = Path.Combine(_dir, "run.tsv");
        await File.WriteAllTextAsync(rcpx, "<Recipe/>");
        await File.WriteAllTextAsync(journal, "{}\n");
        await File.WriteAllTextAsync(tsv, "t\tv\n");

        var bundle = new ElnRunBundle("run-001", "RuC", "complete", rcpx, journal, tsv);

        string exportDir = Path.Combine(_dir, "eln-export");
        var localClient = new ElnSyncClient(new ElnConfig(EndpointUri: null, ApiKeyEnvVar: null, LocalExportDir: exportDir));
        var localRes = await localClient.PublishRunAsync(bundle);
        Assert.False(localRes.Uploaded);
        Assert.Equal("local_bundle", localRes.Mode);
        Assert.True(File.Exists(Path.Combine(localRes.OutputLocation, "bundle.json")));
        Assert.True(File.Exists(Path.Combine(localRes.OutputLocation, "SHA256SUMS.txt")));

        Environment.SetEnvironmentVariable("APS_TEST_ELN_KEY", "eln-token-999");
        try
        {
            var handler = new StubHttpHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"itemId":1600}""", Encoding.UTF8, "application/json"),
            });
            var httpClient = new ElnSyncClient(
                new ElnConfig(new Uri("http://127.0.0.1:9000/api/runs"), "APS_TEST_ELN_KEY", exportDir),
                new HttpClient(handler));
            var remoteRes = await httpClient.PublishRunAsync(bundle);
            Assert.True(remoteRes.Uploaded);
            Assert.Equal("http", remoteRes.Mode);
            Assert.Equal("Bearer eln-token-999", handler.Requests[0].Headers["Authorization"]);
            Assert.Contains("run-001", handler.Requests[0].Body);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APS_TEST_ELN_KEY", null);
        }
    }

    [Fact]
    public async Task AgentSessionRunner_executes_end_to_end_turn_loop_with_batch_denial_recovery_and_journal()
    {
        var emptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        var uploadSchema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"},"content":{"type":"string"}},"required":["name","content"]}""").RootElement;
        var invoker = new FakeMcpInvoker(
            [
                new ToolDeclaration("describe_machine", "Describe", emptySchema),
                new ToolDeclaration("validate_recipe", "Validate", JsonDocument.Parse("""{"type":"object","properties":{"content":{"type":"string"}},"required":["content"]}""").RootElement),
                new ToolDeclaration("upload_recipe", "Upload", uploadSchema),
            ],
            (name, _) => name switch
            {
                "describe_machine" => new ToolCallReceipt("""{"profile":"ADS","sessionTier":"Recipe"}""", false),
                "validate_recipe" => new ToolCallReceipt("""{"valid":true,"findings":[]}""", false),
                "upload_recipe" => new ToolCallReceipt("""{"saved":true}""", false),
                _ => new ToolCallReceipt("unknown", true),
            });

        var model = new ScriptedModelProvider(
            new ProviderTurnResponse("", "need validate", [], [
                new ProviderToolCall("c1", "describe_machine", "{}"),
                new ProviderToolCall("c2", "upload_recipe", """{"name":"R","content":"<Recipe/>"}"""),
            ], new ProviderUsage(20, 5, 25), "tool_calls"),
            new ProviderTurnResponse("", "validate first", [], [
                new ProviderToolCall("c3", "validate_recipe", """{"content":"<Recipe/>"}"""),
            ], new ProviderUsage(25, 5, 30), "tool_calls"),
            new ProviderTurnResponse("", "now upload", [], [
                new ProviderToolCall("c4", "upload_recipe", """{"name":"R","content":"<Recipe/>"}"""),
            ], new ProviderUsage(30, 5, 35), "tool_calls"),
            new ProviderTurnResponse("Done.", null, [], [], new ProviderUsage(35, 3, 38), "stop"));

        string journalPath = Path.Combine(_dir, "session.jsonl");
        var runner = new AgentSessionRunner(model, invoker, new SessionOptions(
            Mode: AgentMode.Autonomous,
            Protocol: ProviderProtocol.ChatCompletions,
            SystemPrompt: "Rules",
            SessionTier: "Recipe",
            RequireValidatedUpload: true,
            MaxTurns: 10,
            ContextBudgetTokens: 32000));

        var result = await runner.RunAsync("Upload R", journalPath);
        Assert.Equal("completed", result.Status);
        Assert.Equal("Done.", result.FinalAnswer);
        Assert.Equal(["validate_recipe", "upload_recipe"], invoker.ExecutedCalls);
        string journalText = await File.ReadAllTextAsync(journalPath);
        Assert.Contains("assistant_batch_denied", journalText);
        Assert.Contains("validate first", journalText);
    }
}
