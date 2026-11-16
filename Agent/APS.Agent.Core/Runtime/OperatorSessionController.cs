using System.Globalization;
using System.Text;
using System.Text.Json;
using APS.Agent.Core.Eln;
using APS.Agent.Core.Guards;
using APS.Agent.Core.Providers;
using APS.Agent.Core.Serialization;
using APS.Agent.Core.Skills;

namespace APS.Agent.Core.Runtime;

public sealed class OperatorSessionController
{
    private readonly object _sync = new();
    private CancellationTokenSource? _runCts;
    private bool _isRunning;

    public AgentMode Mode { get; set; } = AgentMode.Copilot;
    public string SessionTier { get; set; } = "Read";
    public bool AllowDirectDeviceCommand { get; set; }
    public WatchProgressNotice? LatestWatchProgress { get; private set; }
    public string? LastJournalPath { get; private set; }
    public string? LastTelemetryTsvPath { get; private set; }
    public string? LastUploadedRecipeName { get; private set; }
    public string? LastUploadedRecipeContent { get; private set; }
    public string? LastOutcome { get; private set; }

    public bool IsRunning
    {
        get { lock (_sync) return _isRunning; }
    }

    public async Task<MachineIdentityBadge> RefreshMachineBadgeAsync(IMcpToolInvoker invoker, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(invoker);
        using var emptyArgs = JsonDocument.Parse("{}");
        ToolCallReceipt receipt = await invoker.CallAsync("describe_machine", emptyArgs.RootElement, ct).ConfigureAwait(false);
        if (receipt.IsError)
            return MachineIdentityBadge.Offline(SessionTier, receipt.Text);

        var badge = MachineIdentityBadge.FromDescribeMachineJson(receipt.Text, SessionTier);
        SessionTier = badge.SessionTier;
        return badge;
    }

    public async Task<SessionResult> RunGoalAsync(
        string prompt,
        IModelProvider provider,
        IMcpToolInvoker invoker,
        SessionOptions baseOptions,
        string journalPath,
        IAgentSessionObserver? uiObserver = null,
        CancellationToken externalCt = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(invoker);
        ArgumentNullException.ThrowIfNull(baseOptions);

        CancellationTokenSource linkedCts;
        lock (_sync)
        {
            if (_isRunning)
                throw new InvalidOperationException("An operator session is already running.");
            _isRunning = true;
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(externalCt);
            _runCts = linkedCts;
            LatestWatchProgress = null;
            LastJournalPath = journalPath;
            LastTelemetryTsvPath = null;
            LastUploadedRecipeName = null;
            LastUploadedRecipeContent = null;
            LastOutcome = null;
        }

        try
        {
            var trackingObserver = new TrackingObserver(this, uiObserver);
            var effectiveOptions = baseOptions with
            {
                Mode = Mode,
                SessionTier = Mode == AgentMode.Copilot ? "Read" : SessionTier,
                AllowDirectDeviceCommand = Mode == AgentMode.Autonomous && AllowDirectDeviceCommand,
                RequireValidatedUpload = Mode == AgentMode.Autonomous && baseOptions.RequireValidatedUpload,
                Observer = trackingObserver,
            };

            var runner = new AgentSessionRunner(provider, invoker, effectiveOptions);
            SessionResult result = await runner.RunAsync(prompt, journalPath, linkedCts.Token).ConfigureAwait(false);
            LastOutcome = result.Status;
            return result;
        }
        catch (OperationCanceledException)
        {
            LastOutcome = "cancelled";
            throw;
        }
        catch
        {
            LastOutcome = "failed";
            throw;
        }
        finally
        {
            lock (_sync)
            {
                _isRunning = false;
                if (ReferenceEquals(_runCts, linkedCts))
                    _runCts = null;
            }
            linkedCts.Dispose();
        }
    }

    public async Task<ToolCallReceipt> RequestAbortRecipeAsync(IMcpToolInvoker abortInvoker, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(abortInvoker);
        using var emptyArgs = JsonDocument.Parse("{}");
        return await abortInvoker.CallAsync("abort_recipe", emptyArgs.RootElement, ct).ConfigureAwait(false);
    }

    public void ForceStop()
    {
        lock (_sync)
        {
            _runCts?.Cancel();
        }
    }

    public async Task<ElnSyncResult> SyncLatestRunToElnAsync(
        ElnSyncClient elnClient,
        string workDir,
        string? runIdOverride = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(elnClient);
        if (string.IsNullOrWhiteSpace(LastJournalPath) && string.IsNullOrWhiteSpace(LastTelemetryTsvPath) && string.IsNullOrWhiteSpace(LastUploadedRecipeContent))
            throw new InvalidOperationException("No session artifacts are available to sync.");

        Directory.CreateDirectory(workDir);
        string? recipePath = null;
        if (!string.IsNullOrWhiteSpace(LastUploadedRecipeContent))
        {
            string safeStem = SanitizeFileStem(LastUploadedRecipeName ?? "operator-recipe");
            recipePath = Path.Combine(workDir, $"{safeStem}.rcpx");
            await File.WriteAllTextAsync(recipePath, LastUploadedRecipeContent, new UTF8Encoding(false), ct).ConfigureAwait(false);
        }

        string runId = !string.IsNullOrWhiteSpace(runIdOverride)
            ? runIdOverride
            : !string.IsNullOrWhiteSpace(LastJournalPath)
                ? Path.GetFileNameWithoutExtension(LastJournalPath)
                : DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

        var bundle = new ElnRunBundle(
            runId,
            LastUploadedRecipeName ?? "operator-session",
            LastOutcome ?? "unknown",
            recipePath,
            LastJournalPath,
            LastTelemetryTsvPath);
        return await elnClient.PublishRunAsync(bundle, ct).ConfigureAwait(false);
    }

    private static string SanitizeFileStem(string rawName)
    {
        string name = Path.GetFileName(rawName.Trim());
        foreach (char bad in Path.GetInvalidFileNameChars())
            name = name.Replace(bad, '_');
        return string.IsNullOrWhiteSpace(name) ? "operator-recipe" : name;
    }

    private sealed class TrackingObserver(OperatorSessionController owner, IAgentSessionObserver? inner) : IAgentSessionObserver
    {
        private readonly Dictionary<string, (string Name, string Content)> _pendingUploads = new(StringComparer.Ordinal);

        public void OnTurnStarted(int turnIndex) => inner?.OnTurnStarted(turnIndex);

        public void OnAssistantResponse(int turnIndex, ProviderTurnResponse response) => inner?.OnAssistantResponse(turnIndex, response);

        public void OnToolCallStarted(string callId, string toolName, string argumentsJson)
        {
            if (toolName == "upload_recipe")
            {
                try
                {
                    using var doc = StrictJson.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson);
                    string rName = doc.RootElement.TryGetProperty("name", out var np) && np.ValueKind == JsonValueKind.String
                        ? np.GetString() ?? ""
                        : "";
                    string rContent = doc.RootElement.TryGetProperty("content", out var cp) && cp.ValueKind == JsonValueKind.String
                        ? cp.GetString() ?? ""
                        : "";
                    _pendingUploads[callId] = (rName, rContent);
                }
                catch
                {
                }
            }
            inner?.OnToolCallStarted(callId, toolName, argumentsJson);
        }

        public void OnWatchProgress(WatchProgressNotice notice)
        {
            owner.LatestWatchProgress = notice;
            inner?.OnWatchProgress(notice);
        }

        public void OnToolCallCompleted(string callId, string toolName, ToolCallReceipt receipt)
        {
            if (toolName == "upload_recipe" && _pendingUploads.Remove(callId, out var uploaded) && !receipt.IsError)
            {
                if (!string.IsNullOrWhiteSpace(uploaded.Name))
                    owner.LastUploadedRecipeName = uploaded.Name;
                if (!string.IsNullOrWhiteSpace(uploaded.Content))
                    owner.LastUploadedRecipeContent = uploaded.Content;
            }
            else if (toolName == "watch_recipe" && !receipt.IsError)
            {
                try
                {
                    using var doc = StrictJson.Parse(receipt.Text);
                    if (doc.RootElement.TryGetProperty("telemetryTsvPath", out var tsvProp) && tsvProp.ValueKind == JsonValueKind.String)
                    {
                        string? path = tsvProp.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                            owner.LastTelemetryTsvPath = path;
                    }
                }
                catch
                {
                }
            }
            inner?.OnToolCallCompleted(callId, toolName, receipt);
        }

        public void OnSessionCompleted(SessionResult result)
        {
            owner.LastOutcome = result.Status;
            inner?.OnSessionCompleted(result);
        }
    }
}
