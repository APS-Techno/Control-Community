using System.Diagnostics;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Journal;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Runtime;
using APS.Agent.Core.Skills;
using APS.Agent.Core.Strata;

namespace APS.Agent.Cli.Commands;

public sealed record OperatorCliOptions(
    AgentMode Mode,
    ProviderProtocol Protocol,
    Uri Endpoint,
    string? ModelId,
    string? ApiKeyEnvVar = null,
    bool AllowExternalEndpoints = false,
    string McpBin = "APS.Mcp.exe",
    string? McpConfigPath = null,
    string? SkillDir = null,
    string? JournalPath = null,
    string? Prompt = null,
    bool ElnSync = false,
    bool ManageStrata = false,
    string? StrataExe = null,
    string? StrataConfig = null,
    int MaxTurns = 40,
    int ContextBudgetTokens = 65536,
    int? MaxReasoningTokens = null,
    string? ReasoningEffort = null,
    bool AllowDirectDeviceCommand = false);

public static class OperatorCommands
{
    public static readonly Uri DefaultLocalEndpoint = new("http://127.0.0.1:8080/v1");

    public static async Task<int> RunSingleAsync(
        OperatorCliOptions opts,
        TextWriter output,
        TextWriter error,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(opts.Prompt))
        {
            await error.WriteLineAsync("run requires --prompt <text>.").ConfigureAwait(false);
            return 2;
        }

        await using var ctx = await CreateRuntimeContextAsync(opts, ct).ConfigureAwait(false);
        string journalPath = ResolveJournalPath(opts.JournalPath);
        string telemetryDir = Path.Combine(Path.GetDirectoryName(journalPath)!, "telemetry");
        Directory.CreateDirectory(telemetryDir);

        var sessionOpts = BuildSessionOptions(opts, ctx.Skill, ctx.StrataNCtx);
        var sw = Stopwatch.StartNew();
        var runner = new AgentSessionRunner(ctx.Provider, ctx.Mcp, sessionOpts);
        SessionResult res = await runner.RunAsync(opts.Prompt, journalPath, ct).ConfigureAwait(false);
        sw.Stop();

        string runId = Path.GetFileNameWithoutExtension(journalPath);
        AuditJournalWriter.WriteSidecar(
            Path.ChangeExtension(journalPath, ".sidecar.json"),
            new AuditSidecar(
                runId,
                "operator",
                ctx.ModelId,
                opts.ReasoningEffort ?? "none",
                res.Status,
                res.FinalAnswer,
                sw.Elapsed.TotalSeconds,
                res.TotalCostUsd,
                res.ToolCallCount,
                "",
                BenchCommands.ComputeSchemaDigest(ctx.Mcp.Declarations),
                ctx.Skill.SkillDigestSha256,
                Path.GetFileName(journalPath),
                res.JournalSha256,
                res.UsageRecords,
                ErrorCategory: res.ErrorCategory));

        if (!string.IsNullOrEmpty(res.FinalAnswer))
            await output.WriteLineAsync(res.FinalAnswer).ConfigureAwait(false);

        if (opts.ElnSync)
        {
            var eln = new ElnSyncClient(ElnConfig.FromEnvironment(Path.Combine(Path.GetDirectoryName(journalPath)!, "eln")));
            string? latestTsv = Directory.Exists(telemetryDir)
                ? Directory.EnumerateFiles(telemetryDir, "*.tsv").OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault()
                : null;
            ElnSyncResult synced = await eln.PublishRunAsync(
                new ElnRunBundle(runId, "operator-session", res.Status, null, journalPath, latestTsv),
                ct).ConfigureAwait(false);
            await output.WriteLineAsync($"ELN sync ({synced.Mode}): {synced.OutputLocation}").ConfigureAwait(false);
        }

        return res.Status == "completed" ? 0 : 1;
    }

    public static async Task<int> RunReplAsync(
        OperatorCliOptions opts,
        TextReader input,
        TextWriter output,
        CancellationToken ct = default)
    {
        await using var ctx = await CreateRuntimeContextAsync(opts, ct).ConfigureAwait(false);
        var sessionOpts = BuildSessionOptions(opts, ctx.Skill, ctx.StrataNCtx);
        string baseJournal = ResolveJournalPath(opts.JournalPath);
        string dir = Path.GetDirectoryName(baseJournal)!;
        string stem = Path.GetFileNameWithoutExtension(baseJournal);

        await output.WriteLineAsync($"APS Agent REPL ({opts.Mode}, model={ctx.ModelId}). Type /exit to quit.").ConfigureAwait(false);
        int turn = 0;
        while (!ct.IsCancellationRequested)
        {
            await output.WriteAsync("> ").ConfigureAwait(false);
            string? line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Equals("/exit", StringComparison.OrdinalIgnoreCase)
                || trimmed.Equals("/quit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            turn++;
            string turnJournal = Path.Combine(dir, $"{stem}-{turn:D3}.jsonl");
            var runner = new AgentSessionRunner(ctx.Provider, ctx.Mcp, sessionOpts);
            SessionResult res = await runner.RunAsync(trimmed, turnJournal, ct).ConfigureAwait(false);
            await output.WriteLineAsync(res.FinalAnswer).ConfigureAwait(false);
            if (res.Status != "completed")
                await output.WriteLineAsync($"[{res.Status}: {res.ErrorCategory ?? "unknown"}]").ConfigureAwait(false);
        }
        return 0;
    }

    private sealed class RuntimeContext(
        SkillBundle skill,
        IModelProvider provider,
        string modelId,
        StdioMcpToolInvoker mcp,
        StrataSupervisor? strata,
        int? strataNCtx) : IAsyncDisposable
    {
        public SkillBundle Skill => skill;
        public IModelProvider Provider => provider;
        public string ModelId => modelId;
        public StdioMcpToolInvoker Mcp => mcp;
        public int? StrataNCtx => strataNCtx;

        public async ValueTask DisposeAsync()
        {
            await mcp.DisposeAsync().ConfigureAwait(false);
            if (strata is not null)
                await strata.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<RuntimeContext> CreateRuntimeContextAsync(OperatorCliOptions opts, CancellationToken ct)
    {
        EndpointGuard.Validate(opts.Endpoint, opts.AllowExternalEndpoints);
        SkillBundle skill = SkillBundleLoader.Load(opts.SkillDir);

        StrataSupervisor? strata = null;
        int? strataNCtx = null;
        string? resolvedModel = opts.ModelId;

        if (EndpointGuard.IsLoopbackHost(opts.Endpoint.Host))
        {
            StrataModelInfo info;
            if (opts.ManageStrata)
            {
                strata = new StrataSupervisor();
                info = await strata.StartManagedAsync(
                    opts.Endpoint,
                    opts.StrataExe,
                    configPath: opts.StrataConfig,
                    ct: ct).ConfigureAwait(false);
            }
            else
            {
                info = await StrataSupervisor.ProbeRunningAsync(opts.Endpoint, ct: ct).ConfigureAwait(false);
            }
            resolvedModel ??= info.ModelId;
            if (info.NCtx > 0) strataNCtx = info.NCtx;
        }

        if (string.IsNullOrWhiteSpace(resolvedModel))
            throw new InvalidOperationException("Model id is required (--model) for non-Strata endpoints.");

        var providerConfig = new ProviderConfig(
            opts.Protocol,
            opts.Endpoint,
            resolvedModel,
            opts.ApiKeyEnvVar,
            opts.AllowExternalEndpoints,
            opts.MaxReasoningTokens,
            MaxOutputTokens: 8192,
            ReasoningEffort: opts.ReasoningEffort);
        IModelProvider provider = ModelProviderFactory.Create(providerConfig);

        var mcpArgs = new List<string>();
        if (!string.IsNullOrWhiteSpace(opts.McpConfigPath))
        {
            mcpArgs.Add("--config");
            mcpArgs.Add(Path.GetFullPath(opts.McpConfigPath));
        }
        string? telemetryDir = Path.GetDirectoryName(ResolveJournalPath(opts.JournalPath));
        var mcpEnv = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["APS_MCP_TELEMETRY_DIR"] = string.IsNullOrWhiteSpace(telemetryDir) ? null : Path.Combine(telemetryDir, "telemetry"),
        };
        StdioMcpToolInvoker mcp = await StdioMcpToolInvoker.ConnectAsync(
            opts.McpBin,
            mcpArgs,
            mcpEnv,
            Path.GetDirectoryName(opts.McpBin),
            ct).ConfigureAwait(false);

        return new RuntimeContext(skill, provider, resolvedModel, mcp, strata, strataNCtx);
    }

    public static SessionOptions BuildSessionOptions(OperatorCliOptions opts, SkillBundle skill, int? strataNCtx) =>
        new(
            opts.Mode,
            opts.Protocol,
            skill.CombinedPromptText,
            SessionTier: opts.Mode == AgentMode.Copilot ? "Read" : (opts.AllowDirectDeviceCommand ? "Manual" : "Recipe"),
            AllowDirectDeviceCommand: opts.AllowDirectDeviceCommand,
            RequireValidatedUpload: opts.Mode == AgentMode.Autonomous,
            MaxTurns: opts.MaxTurns,
            ContextBudgetTokens: opts.ContextBudgetTokens,
            StrataNCtx: strataNCtx,
            StripHistoricalThinking: true,
            SkillDigest: skill.SkillDigestSha256,
            SkillReadTool: new SandboxedSkillReadTool(skill.RootDirectory));

    private static string ResolveJournalPath(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
            return Path.GetFullPath(requested);
        string stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
        return Path.Combine(Path.GetTempPath(), "aps-agent-runs", $"session-{stamp}.jsonl");
    }
}
