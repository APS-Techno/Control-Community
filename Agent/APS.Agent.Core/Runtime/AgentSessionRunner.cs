using System.Diagnostics;
using System.Text.Json;
using APS.Agent.Core.Context;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Schemas;
using APS.Agent.Core.Skills;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace APS.Agent.Core.Runtime;

public interface IMcpToolInvoker
{
    IReadOnlyList<ToolDeclaration> Declarations { get; }
    Task<ToolCallReceipt> CallAsync(string name, JsonElement arguments, CancellationToken ct);
}

public sealed record SessionOptions(
    AgentMode Mode,
    ProviderProtocol Protocol,
    string SystemPrompt,
    string SessionTier = "Read",
    bool AllowDirectDeviceCommand = false,
    bool RequireValidatedUpload = true,
    IReadOnlySet<string>? BenchmarkAllowedTools = null,
    int MaxTurns = 40,
    int ContextBudgetTokens = 65536,
    int? StrataNCtx = null,
    bool StripHistoricalThinking = true,
    string ConfigDigest = "",
    string SkillDigest = "",
    SandboxedSkillReadTool? SkillReadTool = null);

public sealed record SessionResult(
    string Status,
    string FinalAnswer,
    int ToolCallCount,
    double TotalCostUsd,
    IReadOnlyList<UsageRecord> UsageRecords,
    string? ErrorCategory,
    string JournalSha256);

public sealed class StdioMcpToolInvoker : IMcpToolInvoker, IAsyncDisposable
{
    private readonly McpClient _client;
    public IReadOnlyList<ToolDeclaration> Declarations { get; }

    private StdioMcpToolInvoker(McpClient client, IReadOnlyList<ToolDeclaration> declarations)
    {
        _client = client;
        Declarations = declarations;
    }

    public static async Task<StdioMcpToolInvoker> ConnectAsync(
        string command,
        IReadOnlyList<string> args,
        IDictionary<string, string?>? env = null,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        var opts = new StdioClientTransportOptions
        {
            Command = command,
            Arguments = args.ToList(),
            Name = "aps-control",
            WorkingDirectory = workingDirectory,
        };
        if (env is not null)
        {
            opts.EnvironmentVariables ??= new Dictionary<string, string?>();
            foreach (var kv in env)
                opts.EnvironmentVariables[kv.Key] = kv.Value;
        }
        var transport = new StdioClientTransport(opts);
        var client = await McpClient.CreateAsync(transport, cancellationToken: ct).ConfigureAwait(false);
        var listed = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        var decls = listed.Select(t => new ToolDeclaration(
            t.Name,
            t.Description ?? "",
            t.JsonSchema)).ToList();
        ToolSchemaValidator.ValidateToolSchemas(decls);
        return new StdioMcpToolInvoker(client, decls);
    }

    public async Task<ToolCallReceipt> CallAsync(string name, JsonElement arguments, CancellationToken ct)
    {
        var dict = new Dictionary<string, object?>();
        if (arguments.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in arguments.EnumerateObject())
                dict[p.Name] = JsonSerializer.Deserialize<object>(p.Value.GetRawText());
        }
        CallToolResult res = await _client.CallToolAsync(name, dict, cancellationToken: ct).ConfigureAwait(false);
        string text = string.Join("\n", res.Content
            .OfType<TextContentBlock>()
            .Select(b => b.Text));
        return new ToolCallReceipt(text, res.IsError ?? false);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

public sealed class AgentSessionRunner(
    IModelProvider provider,
    IMcpToolInvoker mcpInvoker,
    SessionOptions options)
{
    public async Task<SessionResult> RunAsync(
        string userPrompt,
        string journalPath,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var allDecls = mcpInvoker.Declarations.ToList();
        if (options.SkillReadTool is not null && allDecls.All(d => d.Name != "skill_read"))
        {
            var skillSchema = JsonDocument.Parse(
                """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"],"additionalProperties":false}""").RootElement;
            allDecls.Add(new ToolDeclaration("skill_read", "Read a file inside the aps-operator skill snapshot.", skillSchema, "local"));
        }

        ToolSchemaValidator.ValidateToolSchemas(allDecls);
        var exposedDecls = ToolPolicy.FilterDeclarations(
            options.Mode,
            allDecls,
            options.SessionTier,
            options.AllowDirectDeviceCommand,
            options.BenchmarkAllowedTools);
        var declMap = exposedDecls.ToDictionary(d => d.Name, StringComparer.Ordinal);
        var allowedSet = declMap.Keys.ToHashSet(StringComparer.Ordinal);
        string toolSchemaDigest = ValidatedUploadGate.ComputeDigest(
            JsonSerializer.Serialize(exposedDecls.Select(d => new { d.Name, d.Description, d.Parameters })));

        var uploadGate = new ValidatedUploadGate();
        var history = new List<ConversationMessage> { new("user", userPrompt, Pinned: true) };
        var usageRecords = new List<UsageRecord>();
        int budget = ContextCompactor.ComputeBudget(options.ContextBudgetTokens, options.StrataNCtx);
        int? lastPromptTokens = null;
        int lastPromptMsgCount = 0;
        int toolCallsCount = 0;
        double totalCost = 0.0;
        string status = "completed";
        string finalAnswer = "";
        string? errorCategory = null;

        await using (var journal = new AuditJournalWriter(journalPath))
        {
            await journal.WriteEventAsync("trial_start", new
            {
                ask = userPrompt,
                mode = options.Mode.ToString(),
                config_digest = options.ConfigDigest,
                tool_schema_digest = toolSchemaDigest,
                skill_digest = options.SkillDigest,
            }, ct).ConfigureAwait(false);

            for (int turn = 0; turn < options.MaxTurns; turn++)
            {
                var prepared = ContextCompactor.PrepareHistory(
                    options.Protocol,
                    history,
                    budget,
                    lastPromptTokens,
                    lastPromptMsgCount,
                    options.SystemPrompt,
                    options.StripHistoricalThinking);

                var response = await provider.CompleteAsync(
                    new ProviderTurnRequest(options.SystemPrompt, prepared, exposedDecls), ct).ConfigureAwait(false);

                lastPromptTokens = response.Usage.InputTokens;
                lastPromptMsgCount = history.Count;
                double turnCost = response.Usage.EstimatedCostUsd ?? 0.0;
                totalCost += turnCost;
                string usageKey = $"turn_{turn}";
                usageRecords.Add(new UsageRecord(usageKey, turnCost));
                await journal.WriteUsageAsync(usageKey, turnCost, response.Usage, ct).ConfigureAwait(false);
                await journal.WriteEventAsync("assistant_turn", new
                {
                    turn,
                    text = response.AssistantText,
                    reasoning = response.ReasoningText,
                    stop_reason = response.StopReason,
                    tool_calls = response.ToolCalls.Select(tc => new { tc.CallId, tc.Name }),
                }, ct).ConfigureAwait(false);

                if (response.ToolCalls.Count == 0)
                {
                    finalAnswer = response.AssistantText;
                    break;
                }

                var pending = response.ToolCalls
                    .Select(tc => new PendingToolCall(tc.CallId, tc.Name, tc.ArgumentsJson))
                    .ToList();

                history.Add(new ConversationMessage(
                    "assistant",
                    response.AssistantText,
                    response.ReasoningText,
                    response.ReasoningBlocks,
                    response.ToolCalls));

                var batchDecision = ToolBatchGuard.Evaluate(options.Mode, pending, allowedSet);
                if (!batchDecision.Allowed)
                {
                    string eventType = batchDecision.AbortTrial ? "guard_denied" : "denied";
                    await journal.WriteEventAsync(eventType, new
                    {
                        category = batchDecision.DenialCategory,
                        message = batchDecision.ErrorMessage,
                    }, ct).ConfigureAwait(false);

                    if (batchDecision.AbortTrial)
                    {
                        status = "failed";
                        errorCategory = batchDecision.DenialCategory;
                        break;
                    }

                    foreach (var tc in response.ToolCalls)
                    {
                        history.Add(new ConversationMessage(
                            "tool_result",
                            batchDecision.ErrorMessage ?? batchDecision.DenialCategory,
                            ToolCallId: tc.CallId,
                            ToolName: tc.Name,
                            IsError: true));
                    }
                    continue;
                }

                foreach (var tc in response.ToolCalls)
                {
                    toolCallsCount++;
                    ToolCallReceipt receipt;
                    try
                    {
                        using var argDoc = Serialization.StrictJson.Parse(
                            string.IsNullOrWhiteSpace(tc.ArgumentsJson) ? "{}" : tc.ArgumentsJson);
                        var cleanArgs = ToolSchemaValidator.ValidateArguments(declMap[tc.Name], argDoc.RootElement);
                        await journal.WriteEventAsync("tool_call", new
                        {
                            call_id = tc.CallId,
                            name = tc.Name,
                            arguments = JsonSerializer.Deserialize<object>(cleanArgs.GetRawText()),
                        }, ct).ConfigureAwait(false);

                        if (tc.Name == "upload_recipe")
                        {
                            string recipeContent = cleanArgs.TryGetProperty("content", out var cp) ? cp.GetString() ?? "" : "";
                            var (ok, errCode, errMsg) = uploadGate.CanUpload(recipeContent, options.RequireValidatedUpload);
                            receipt = ok
                                ? await InvokeSingleToolAsync(tc.Name, cleanArgs, ct).ConfigureAwait(false)
                                : new ToolCallReceipt($"{errCode}: {errMsg}", true);
                        }
                        else
                        {
                            receipt = await InvokeSingleToolAsync(tc.Name, cleanArgs, ct).ConfigureAwait(false);
                            if (tc.Name == "validate_recipe" && !receipt.IsError)
                            {
                                string checkedContent = cleanArgs.TryGetProperty("content", out var cp) ? cp.GetString() ?? "" : "";
                                bool valid = TryReadValidFlag(receipt.Text);
                                uploadGate.RecordValidationResult(checkedContent, valid);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        receipt = new ToolCallReceipt(ex.Message, true);
                    }

                    await journal.WriteEventAsync("tool_result", new
                    {
                        call_id = tc.CallId,
                        name = tc.Name,
                        result = new { isError = receipt.IsError, text = receipt.Text },
                    }, ct).ConfigureAwait(false);

                    history.Add(new ConversationMessage(
                        "tool_result",
                        receipt.Text,
                        ToolCallId: tc.CallId,
                        ToolName: tc.Name,
                        IsError: receipt.IsError));
                }

                if (turn == options.MaxTurns - 1)
                {
                    status = "max_turns";
                    errorCategory = "max_turns";
                }
            }

            await journal.WriteEventAsync("trial_end", new
            {
                status,
                duration_s = sw.Elapsed.TotalSeconds,
                tool_call_count = toolCallsCount,
            }, ct).ConfigureAwait(false);
        }

        string sha = AuditJournalWriter.ComputeFileSha256(journalPath);
        return new SessionResult(status, finalAnswer, toolCallsCount, totalCost, usageRecords, errorCategory, sha);
    }

    private Task<ToolCallReceipt> InvokeSingleToolAsync(string name, JsonElement cleanArgs, CancellationToken ct)
    {
        if (name == "skill_read" && options.SkillReadTool is not null)
        {
            string reqPath = cleanArgs.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            return Task.FromResult(options.SkillReadTool.Read(reqPath));
        }
        return mcpInvoker.CallAsync(name, cleanArgs, ct);
    }

    private static bool TryReadValidFlag(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return doc.RootElement.TryGetProperty("valid", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch
        {
            return false;
        }
    }
}
