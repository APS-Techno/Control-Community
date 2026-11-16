using System.Net;
using System.Text;
using System.Text.Json;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Skills;

namespace APS.Agent.Tests;

public sealed class OperatorUiControllerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aps-ui-ctrl-test-{Guid.NewGuid():N}");

    public OperatorUiControllerTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private sealed class StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class MockMcpInvoker : IMcpToolInvoker
    {
        private readonly Func<string, JsonElement, IProgress<WatchProgressNotice>?, CancellationToken, Task<ToolCallReceipt>> _handler;
        public readonly List<(string Name, string ArgsJson)> Calls = [];

        public MockMcpInvoker(
            IReadOnlyList<ToolDeclaration> decls,
            Func<string, JsonElement, IProgress<WatchProgressNotice>?, CancellationToken, Task<ToolCallReceipt>> handler)
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
            Calls.Add((name, arguments.GetRawText()));
            return _handler(name, arguments, watchProgress, ct);
        }
    }

    private sealed class ScriptedProvider(
        Func<ProviderTurnRequest, CancellationToken, Task<ProviderTurnResponse>> handler) : IModelProvider
    {
        public Task<ProviderTurnResponse> CompleteAsync(ProviderTurnRequest request, CancellationToken ct = default) =>
            handler(request, ct);
    }

    private sealed class RecordingObserver : IAgentSessionObserver
    {
        public readonly List<WatchProgressNotice> ProgressNotices = [];
        public readonly List<string> CompletedTools = [];
        public SessionResult? CompletedSession;

        public void OnWatchProgress(WatchProgressNotice notice) => ProgressNotices.Add(notice);
        public void OnToolCallCompleted(string callId, string toolName, ToolCallReceipt receipt) => CompletedTools.Add(toolName);
        public void OnSessionCompleted(SessionResult result) => CompletedSession = result;
    }

    [Fact]
    public void WatchProgressNotice_parses_loop_fraction_deposits_and_telemetry_and_formats_period_label()
    {
        var notice = WatchProgressNotice.Parse(
            10,
            50,
            "Running step=24 iter=5/50 deposits=10 psu=PSU_C:382.5V/50.0mA/19.1W pressure=Main:0.003Torr");

        Assert.Equal("Running", notice.RecipeState);
        Assert.Equal(24L, notice.StepSequence);
        Assert.Equal(5, notice.CurrentLoopIteration);
        Assert.Equal(50, notice.TotalLoopIterations);
        Assert.Equal(10, notice.ExecutedDepositsCount);
        Assert.Equal("Period 5/50", notice.PeriodLabel);
        Assert.Contains("psu=PSU_C:382.5V/50.0mA/19.1W", notice.TelemetrySummary);
        Assert.Contains("pressure=Main:0.003Torr", notice.TelemetrySummary);

        var openEnded = WatchProgressNotice.Parse(2, null, "Running step=3 iter=2 deposits=2");
        Assert.Equal("Period 2", openEnded.PeriodLabel);
        Assert.Equal(0, openEnded.TotalLoopIterations);
    }

    [Fact]
    public void MachineIdentityBadge_parses_emulator_and_hardware_describe_machine_receipts()
    {
        var emu = MachineIdentityBadge.FromDescribeMachineJson("""
            {
              "profile": "ADS",
              "appVersion": "1.7.2",
              "isEmulator": true,
              "executionTarget": "emulator",
              "sessionTier": "Recipe"
            }
            """);

        Assert.True(emu.Connected);
        Assert.True(emu.IsEmulator);
        Assert.Equal("ADS", emu.Profile);
        Assert.Equal("Recipe", emu.SessionTier);
        Assert.Equal("EMULATOR | ADS | TIER: RECIPE", emu.BadgeText);
        Assert.Contains("1.7.2", emu.Tooltip);

        var hw = MachineIdentityBadge.FromDescribeMachineJson("""
            {
              "profile": "Sputter",
              "appVersion": "1.7.2",
              "isEmulator": false,
              "executionTarget": "physical",
              "sessionTier": "Manual"
            }
            """);

        Assert.False(hw.IsEmulator);
        Assert.Equal("HARDWARE | Sputter | TIER: MANUAL", hw.BadgeText);

        var offline = MachineIdentityBadge.Offline("Read", "not connected");
        Assert.False(offline.Connected);
        Assert.Equal("MCP OFFLINE | TIER: READ", offline.BadgeText);
    }

    [Fact]
    public async Task AgentConnectionMonitor_reports_strata_cloud_and_eln3_indicator_states()
    {
        var strataLoaded = new StubHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "object": "list",
                  "data": [{
                    "id": "local-qwen",
                    "status": { "value": "loaded" },
                    "meta": { "n_ctx": 65536 }
                  }]
                }
                """, Encoding.UTF8, "application/json"),
        });

        var localSnap = await AgentConnectionMonitor.EvaluateAsync(
            new Uri("http://127.0.0.1:8081/v1"),
            allowExternalEndpoints: false,
            apiKeyEnvVar: null,
            new ElnConfig(null, null, _dir),
            new HttpClient(strataLoaded));

        Assert.Equal(IndicatorState.Ready, localSnap.Strata.State);
        Assert.Equal("STRATA LOADED", localSnap.Strata.Label);
        Assert.Equal(IndicatorState.Off, localSnap.CloudLlm.State);
        Assert.Equal(IndicatorState.Warning, localSnap.Eln3.State);
        Assert.Equal("ELN3 LOCAL", localSnap.Eln3.Label);

        var lockedCloud = await AgentConnectionMonitor.EvaluateAsync(
            new Uri("https://api.openai.com/v1"),
            allowExternalEndpoints: false,
            apiKeyEnvVar: "APS_MISSING_TEST_KEY",
            new ElnConfig(new Uri("http://127.0.0.1:9000/api/runs"), null, _dir));

        Assert.Equal(IndicatorState.Off, lockedCloud.Strata.State);
        Assert.Equal(IndicatorState.Error, lockedCloud.CloudLlm.State);
        Assert.Equal("CLOUD LOCKED", lockedCloud.CloudLlm.Label);
        Assert.Equal(IndicatorState.Ready, lockedCloud.Eln3.State);
        Assert.Equal("ELN3 HTTP", lockedCloud.Eln3.Label);

        Environment.SetEnvironmentVariable("APS_TEST_CLOUD_KEY", "secret");
        try
        {
            var readyCloud = await AgentConnectionMonitor.EvaluateAsync(
                new Uri("https://api.openai.com/v1"),
                allowExternalEndpoints: true,
                apiKeyEnvVar: "APS_TEST_CLOUD_KEY",
                new ElnConfig(null, null, _dir, McpCommand: "eln-mcp.exe"));

            Assert.Equal(IndicatorState.Ready, readyCloud.CloudLlm.State);
            Assert.Equal(IndicatorState.Ready, readyCloud.Eln3.State);
            Assert.Equal("ELN3 MCP", readyCloud.Eln3.Label);
        }
        finally
        {
            Environment.SetEnvironmentVariable("APS_TEST_CLOUD_KEY", null);
        }
    }

    [Fact]
    public async Task OperatorSessionController_streams_watch_progress_and_out_of_band_abort_wakes_watch_for_closing_summary()
    {
        var emptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        var watchSchema = JsonDocument.Parse("""{"type":"object","properties":{"wake_after_deposits":{"type":"integer"}}}""").RootElement;
        var uploadSchema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"},"content":{"type":"string"}},"required":["name","content"]}""").RootElement;

        var watchStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var abortReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string tsvPath = Path.Combine(_dir, "watch-001.tsv");
        await File.WriteAllTextAsync(tsvPath, "timestampUtc\telapsedS\n");

        var invoker = new MockMcpInvoker(
            [
                new ToolDeclaration("describe_machine", "Describe", emptySchema),
                new ToolDeclaration("validate_recipe", "Validate", JsonDocument.Parse("""{"type":"object","properties":{"content":{"type":"string"}},"required":["content"]}""").RootElement),
                new ToolDeclaration("upload_recipe", "Upload", uploadSchema),
                new ToolDeclaration("watch_recipe", "Watch", watchSchema),
                new ToolDeclaration("abort_recipe", "Abort", emptySchema),
            ],
            async (name, _, progress, ct) =>
            {
                switch (name)
                {
                    case "describe_machine":
                        return new ToolCallReceipt("""{"profile":"ADS","appVersion":"1.7.2","isEmulator":true,"executionTarget":"emulator","sessionTier":"Recipe"}""", false);
                    case "validate_recipe":
                        return new ToolCallReceipt("""{"valid":true}""", false);
                    case "upload_recipe":
                        return new ToolCallReceipt("""{"saved":true}""", false);
                    case "watch_recipe":
                        progress?.Report(WatchProgressNotice.Parse(4, 50, "Running step=12 iter=2/50 deposits=4 psu=PSU_C:380.0V/50.0mA/19.0W pressure=Main:0.002Torr"));
                        watchStarted.TrySetResult();
                        await abortReceived.Task.WaitAsync(ct);
                        return new ToolCallReceipt(JsonSerializer.Serialize(new
                        {
                            wakeReason = "aborted",
                            elapsedSeconds = 12.5,
                            recipeState = "Aborting",
                            stepSequence = 12,
                            currentLoopIteration = 2,
                            executedDepositsCount = 4,
                            telemetryTsvPath = tsvPath,
                        }), false);
                    case "abort_recipe":
                        abortReceived.TrySetResult();
                        return new ToolCallReceipt("""{"accepted":true}""", false);
                    default:
                        return new ToolCallReceipt("unknown", true);
                }
            });

        int turn = 0;
        var provider = new ScriptedProvider((req, _) =>
        {
            turn++;
            return Task.FromResult(turn switch
            {
                1 => new ProviderTurnResponse("", null, [], [new ProviderToolCall("c1", "validate_recipe", """{"content":"<Recipe/>"}""")], new ProviderUsage(10, 5, 15), "tool_calls"),
                2 => new ProviderTurnResponse("", null, [], [new ProviderToolCall("c2", "upload_recipe", """{"name":"RuC_50","content":"<Recipe/>"}""")], new ProviderUsage(15, 5, 20), "tool_calls"),
                3 => new ProviderTurnResponse("", null, [], [new ProviderToolCall("c3", "watch_recipe", """{"wake_after_deposits":10}""")], new ProviderUsage(20, 5, 25), "tool_calls"),
                _ => new ProviderTurnResponse("Run aborted safely after 4 deposits.", null, [], [], new ProviderUsage(25, 5, 30), "stop"),
            });
        });

        var controller = new OperatorSessionController { Mode = AgentMode.Autonomous };
        var badge = await controller.RefreshMachineBadgeAsync(invoker);
        Assert.Equal("Recipe", controller.SessionTier);
        Assert.Equal("EMULATOR | ADS | TIER: RECIPE", badge.BadgeText);

        var observer = new RecordingObserver();
        string journalPath = Path.Combine(_dir, "abort-session.jsonl");
        var runTask = controller.RunGoalAsync(
            "Run Ru/C",
            provider,
            invoker,
            new SessionOptions(AgentMode.Autonomous, ProviderProtocol.ChatCompletions, "Rules", SessionTier: "Recipe"),
            journalPath,
            observer);

        await watchStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var abortReceipt = await controller.RequestAbortRecipeAsync(invoker);
        Assert.False(abortReceipt.IsError);

        SessionResult res = await runTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("completed", res.Status);
        Assert.Equal("Run aborted safely after 4 deposits.", res.FinalAnswer);
        Assert.Single(observer.ProgressNotices);
        Assert.Equal("Period 2/50", observer.ProgressNotices[0].PeriodLabel);
        Assert.Equal(tsvPath, controller.LastTelemetryTsvPath);
        Assert.Equal("RuC_50", controller.LastUploadedRecipeName);
    }

    [Fact]
    public async Task OperatorSessionController_ForceStop_cancels_in_flight_session_immediately()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new ScriptedProvider(async (_, ct) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new ProviderTurnResponse("", null, [], [], new ProviderUsage(0, 0, 0), "stop");
        });
        var invoker = new MockMcpInvoker([], (_, _, _, _) => Task.FromResult(new ToolCallReceipt("{}", false)));

        var controller = new OperatorSessionController { Mode = AgentMode.Copilot };
        var task = controller.RunGoalAsync(
            "Hang forever",
            provider,
            invoker,
            new SessionOptions(AgentMode.Copilot, ProviderProtocol.ChatCompletions, "Rules"),
            Path.Combine(_dir, "force-stop.jsonl"));

        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        controller.ForceStop();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(controller.IsRunning);
        Assert.Equal("cancelled", controller.LastOutcome);
    }

    [Fact]
    public async Task ElnSyncClient_supports_mcp_tool_sync_and_OperatorSessionController_syncs_latest_artifacts()
    {
        string journal = Path.Combine(_dir, "run.jsonl");
        string tsv = Path.Combine(_dir, "watch.tsv");
        await File.WriteAllTextAsync(journal, "{}\n");
        await File.WriteAllTextAsync(tsv, "timestampUtc\n");

        var elnToolSchema = JsonDocument.Parse("""{"type":"object","properties":{"runId":{"type":"string"},"recipeName":{"type":"string"},"outcome":{"type":"string"},"artifacts":{"type":"array"}}}""").RootElement;
        var elnMcp = new MockMcpInvoker(
            [new ToolDeclaration("upload_run_bundle", "Upload run bundle", elnToolSchema)],
            (_, _, _, _) => Task.FromResult(new ToolCallReceipt("""{"itemId":1701}""", false)));

        var client = new ElnSyncClient(
            new ElnConfig(null, null, _dir, McpCommand: "eln-mcp.exe"),
            mcpInvoker: elnMcp);

        var bundle = new ElnRunBundle("run-77", "RuC_50", "completed", null, journal, tsv);
        var res = await client.PublishRunAsync(bundle);
        Assert.True(res.Uploaded);
        Assert.Equal("mcp", res.Mode);
        Assert.Single(elnMcp.Calls);
        Assert.Equal("upload_run_bundle", elnMcp.Calls[0].Name);
        Assert.Contains("run-77", elnMcp.Calls[0].ArgsJson);
        Assert.Contains("RuC_50", elnMcp.Calls[0].ArgsJson);

        var emptySchema = JsonDocument.Parse("""{"type":"object","properties":{}}""").RootElement;
        var uploadSchema = JsonDocument.Parse("""{"type":"object","properties":{"name":{"type":"string"},"content":{"type":"string"}},"required":["name","content"]}""").RootElement;
        var machineMcp = new MockMcpInvoker(
            [
                new ToolDeclaration("validate_recipe", "Validate", JsonDocument.Parse("""{"type":"object","properties":{"content":{"type":"string"}},"required":["content"]}""").RootElement),
                new ToolDeclaration("upload_recipe", "Upload", uploadSchema),
            ],
            (name, _, _, _) => Task.FromResult(name switch
            {
                "validate_recipe" => new ToolCallReceipt("""{"valid":true}""", false),
                "upload_recipe" => new ToolCallReceipt("""{"saved":true}""", false),
                _ => new ToolCallReceipt("unknown", true),
            }));
        int turn = 0;
        var provider = new ScriptedProvider((_, _) =>
        {
            turn++;
            return Task.FromResult(turn switch
            {
                1 => new ProviderTurnResponse("", null, [], [new ProviderToolCall("v1", "validate_recipe", """{"content":"<Recipe/>"}""")], new ProviderUsage(5, 2, 7), "tool_calls"),
                2 => new ProviderTurnResponse("", null, [], [new ProviderToolCall("u1", "upload_recipe", """{"name":"SyncedRecipe","content":"<Recipe/>"}""")], new ProviderUsage(5, 2, 7), "tool_calls"),
                _ => new ProviderTurnResponse("Done.", null, [], [], new ProviderUsage(5, 2, 7), "stop"),
            });
        });

        var controller = new OperatorSessionController { Mode = AgentMode.Autonomous, SessionTier = "Recipe" };
        await controller.RunGoalAsync(
            "Upload",
            provider,
            machineMcp,
            new SessionOptions(AgentMode.Autonomous, ProviderProtocol.ChatCompletions, "Rules", SessionTier: "Recipe"),
            journal);
        var syncRes = await controller.SyncLatestRunToElnAsync(client, Path.Combine(_dir, "sync-work"), "run-78");
        Assert.True(syncRes.Uploaded);
        Assert.Equal(2, elnMcp.Calls.Count);
        Assert.Contains("run-78", elnMcp.Calls[1].ArgsJson);
        Assert.Contains("SyncedRecipe", elnMcp.Calls[1].ArgsJson);
        Assert.Contains("\"kind\":\"recipe\"", elnMcp.Calls[1].ArgsJson);

        controller.Mode = AgentMode.Copilot;
        await controller.RunGoalAsync(
            "Check status",
            new ScriptedProvider((_, _) => Task.FromResult(new ProviderTurnResponse("OK", null, [], [], new ProviderUsage(1, 1, 2), "stop"))),
            machineMcp,
            new SessionOptions(AgentMode.Copilot, ProviderProtocol.ChatCompletions, "Rules"),
            Path.Combine(_dir, "second-run.jsonl"));
        Assert.Null(controller.LastUploadedRecipeName);
        Assert.Null(controller.LastUploadedRecipeContent);
    }
}
