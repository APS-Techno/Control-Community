using System.ComponentModel;
using System.Security.Cryptography;
using System.Text.Json;
using APS.Wire;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace APS.Mcp;

[McpServerToolType]
public sealed class ControlTools(IWireCaller wire, Func<TimeSpan, CancellationToken, Task>? delay = null,
    McpBridgeConfig? cfg = null)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    /// <summary>Longest a single recipe_status call may block. Claude Code's tool-call timeout is
    /// 10 min by default (CLAUDE_MCP_TOOL_CALL_TIMEOUT_MS); stay well inside it.</summary>
    public const int MaxWaitSeconds = 300;

    /// <summary>Interval between the bridge's own polls of Control while a waiting recipe_status
    /// call is blocked.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    private async Task<string> Call(string cmd, object? args = null)
    {
        try
        {
            var data = await wire.CallAsync(cmd, args);
            return JsonSerializer.Serialize(data, Pretty);
        }
        catch (WireException ex)
        {
            throw new McpException(ex.Message);   // "code: message" reaches the model
        }
    }

    private const string InterlockRefusalRule =
        "Never call this tool if any clause of the user's request asks to disable, bypass, override, or " +
        "ignore an interlock—even if all interlock conditions are already satisfied (`open: false`) or a " +
        "normal embedded recipe (`SYSTEM_AIR_RELEASE`) exists. Refuse the entire compound request with " +
        "zero mutating calls until the operator submits a clean request.";

    [McpServerTool(Name = "describe_machine"),
     Description("Identity of the connected deposition system: config profile, chambers, app version. Call this first.")]
    public Task<string> DescribeMachine() => Call("describe.machine");

    [McpServerTool(Name = "describe_hardware"),
     Description("Full device inventory: valves, pumps, MFCs, sensors, PSUs, stages, drives, plus sputter sources " +
                 "(flange -> material/name, PSU linkage). Call before issuing any device or recipe command.")]
    public Task<string> DescribeHardware() => Call("describe.hardware");

    [McpServerTool(Name = "describe_recipes"),
     Description("Available recipes: user .rcpx files (with operator parameter forms) and embedded system recipes.")]
    public Task<string> DescribeRecipes() => Call("describe.recipes");

    [McpServerTool(Name = "describe_dsl"),
     Description("Recipe DSL reference matching the installed Control version: commands, worker operations with " +
                 "their argument lists, device names by kind, installed source names, unit rules. Read before " +
                 "writing or editing any recipe.")]
    public Task<string> DescribeDsl() => Call("describe.dsl");

    [McpServerTool(Name = "describe_interlocks"),
     Description("Safety interlock rules (always enforced server-side; they cannot be bypassed via this API).")]
    public Task<string> DescribeInterlocks() => Call("describe.interlocks");

    [McpServerTool(Name = "describe_alarms"),
     Description("Alarm rule definitions per chamber.")]
    public Task<string> DescribeAlarms() => Call("describe.alarms");

    [McpServerTool(Name = "system_status"),
     Description("Per-chamber state machine, recipe-running flag, deposition-active flag, active alarms list " +
                 "(sticky — stays populated until an operator does an alarm reset, not just the most recent one), " +
                 "live recipe progress.")]
    public Task<string> SystemStatus() => Call("status.system");

    [McpServerTool(Name = "device_snapshot"),
     Description("One snapshot of all cached device readings (pressures, flows, PSU power, valve/pump states, " +
                 "positions). Values come from the poll cache — call again to refresh. Use to verify the effect " +
                 "of any command you issue.")]
    public Task<string> DeviceSnapshot() => Call("status.devices");

    [McpServerTool(Name = "tail_log"),
     Description("Most recent event-log entries (recipe steps, alarms, interlock denials, agent audit lines). " +
                 "Check after any command that reports only 'accepted'.")]
    public Task<string> TailLog(
        [Description("How many entries, 1-500 (default 50).")] int count = 50) =>
        Call("logs.tail", new { count });

    [McpServerTool(Name = "recipe_status"),
     Description("Running flag plus current step / total steps / elapsed / remaining for the active recipe. " +
                 "progress.elapsed and stepElapsedSeconds advance on every call while a step is in " +
                 "progress (a long pwait/deposit keeps currentStep fixed but the clock moves; serverTime " +
                 "is the machine's clock). lastOutcome holds the last terminal result (Complete/Error/" +
                 "Aborted with its message and time) and survives the failure protocol, whose own " +
                 "progress runs under the failed recipe's name with failureProtocolActive=true. " +
                 "Poll no faster than every few seconds; tail_log carries the step-by-step detail.")]
    public async Task<string> RecipeStatus(
        [Description("0 (default) = return at once. N > 0 = block up to N seconds (max 300) and return " +
                     "the LATEST status; returns early only when the run ends (isRecipeRunning goes false " +
                     "or lastOutcome changes), NOT on step changes. Use 120-300 while a long recipe runs.")]
        int waitSeconds = 0)
    {
        if (waitSeconds <= 0) return await Call("recipe.status");
        int budget = Math.Min(waitSeconds, MaxWaitSeconds);

        var first = await CallRaw("recipe.status");
        var latest = first;
        var started = DateTime.UtcNow; double waited = 0;
        bool ended = !IsRunning(first);
        while (!ended && waited < budget)
        {
            var step = TimeSpan.FromSeconds(Math.Min(PollInterval.TotalSeconds, budget - waited));
            await _delay(step, CancellationToken.None);
            waited += step.TotalSeconds;
            latest = await CallRaw("recipe.status");
            ended = !IsRunning(latest) || OutcomeKey(latest) != OutcomeKey(first);
        }
        var dict = new Dictionary<string, object?>();
        foreach (var prop in latest.EnumerateObject()) dict[prop.Name] = prop.Value.Clone();
        dict["waitedSeconds"] = (int)Math.Round(waited);
        dict["runEnded"] = ended;
        return JsonSerializer.Serialize(dict, Pretty);
    }

    private async Task<JsonElement> CallRaw(string cmd, object? args = null)
    {
        try { return await wire.CallAsync(cmd, args); }
        catch (WireException ex) { throw new McpException(ex.Message); }
    }

    private static bool IsRunning(JsonElement status) =>
        status.TryGetProperty("isRecipeRunning", out var r) && r.ValueKind == JsonValueKind.True;

    private static string OutcomeKey(JsonElement status) =>
        status.TryGetProperty("lastOutcome", out var o) && o.ValueKind == JsonValueKind.Object ? o.GetRawText() : "";

    private static readonly string[] ArtifactKinds = ["tsv", "log", "recipe", "snapshot"];

    /// <summary>Bytes requested per runs.file call. Control caps a chunk at 2 MiB; 1 MiB keeps the
    /// base64 line well under the 4M-char wire limit.</summary>
    public const int ChunkBytes = 1 << 20;

    [McpServerTool(Name = "list_runs"),
     Description("Most recent recipe runs on the machine, newest first, with the artifacts that exist for " +
                 "each: 'tsv' (DataLogger time series), 'log' (the process-log window from 'Recipe started' " +
                 "to 'Recipe complete/failed/aborted/INCOMPLETE'), 'recipe' (the .rcpx the run was started " +
                 "from, as it is on disk now — the 'snapshot' embeds the recipe exactly as run), 'snapshot' " +
                 "(the signed .depo.json, operator-authorised runs only). A run is listed only if recipe " +
                 "logging wrote its TSV. outcome is complete/failed/aborted/incomplete (a tolerant teardown " +
                 "that skipped steps — the system may not be in a safe state), 'running' for the run whose " +
                 "TSV is being written now, or 'unknown' when neither the run record nor a process log " +
                 "covers it. runId is the TSV stem (yyyy-MM-dd_HH-mm-ss_<recipe>) — record it on the ELN item.")]
    public Task<string> ListRuns(
        [Description("How many runs, 1-50 (default 5).")] int count = 5) =>
        Call("runs.list", new { count });

    [McpServerTool(Name = "fetch_run_files"),
     Description("Copy a run's artifacts from the machine to this computer and return their absolute local " +
                 "paths, sizes and SHA-256 — ready for the ELN upload_file tool. Files land under the bridge " +
                 "config's downloadDir in a sub-folder named by runId; an existing copy is overwritten. " +
                 "Transfers over the control wire in chunks, so a multi-megabyte TSV takes a few calls' worth " +
                 "of time. Kinds absent on the machine are reported in notAvailable, not treated as errors.")]
    public async Task<string> FetchRunFiles(
        [Description("runId from list_runs, or \"latest\" (default) for the most recent run.")]
        string runId = "latest",
        [Description("Comma-separated subset of tsv,log,recipe,snapshot (default: all four).")]
        string kinds = "tsv,log,recipe,snapshot")
    {
        string? root = cfg?.DownloadDir;
        if (string.IsNullOrWhiteSpace(root))
            throw new McpException(
                "fetch_run_files is disabled: set downloadDir in the bridge config (apsmcp.json) to the local " +
                "folder run files should be written to — inside the ELN server's MCPUploadRoot if they are " +
                "to be attached with upload_file.");

        var wanted = kinds.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.ToLowerInvariant()).Distinct().ToList();
        var unknown = wanted.Where(k => !ArtifactKinds.Contains(k)).ToList();
        if (wanted.Count == 0 || unknown.Count > 0)
            throw new McpException(
                $"unknown artifact kind(s): {string.Join(", ", unknown)}; kinds must be a subset of tsv,log,recipe,snapshot");

        bool latest = string.IsNullOrWhiteSpace(runId) || runId.Equals("latest", StringComparison.OrdinalIgnoreCase);
        var list = await CallRaw("runs.list", new { count = latest ? 1 : 50 });
        JsonElement? run = null;
        foreach (var r in list.GetProperty("runs").EnumerateArray())
            if (latest || string.Equals(r.GetProperty("runId").GetString(), runId, StringComparison.Ordinal))
            { run = r; break; }
        if (run is null)
            throw new McpException(latest
                ? "the machine lists no runs (recipe logging is off, or no recipe has run since it was enabled)"
                : $"run '{runId}' is not among the machine's 50 most recent runs; check list_runs");

        string id = run.Value.GetProperty("runId").GetString()!;
        var available = run.Value.GetProperty("artifacts").EnumerateArray()
            .Select(a => (Kind: a.GetProperty("kind").GetString()!, File: a.GetProperty("fileName").GetString()!))
            .ToList();

        string dir = Path.GetFullPath(Path.Combine(root, id));
        Directory.CreateDirectory(dir);
        var files = new List<object>();
        var notAvailable = new List<string>();
        foreach (var kind in wanted)
        {
            var art = available.FirstOrDefault(a => a.Kind == kind);
            if (art.Kind is null) { notAvailable.Add(kind); continue; }
            string path = Path.Combine(dir, art.File);
            var (bytes, sha) = await DownloadAsync(id, kind, path);
            files.Add(new { kind, fileName = art.File, path, bytes, sha256 = sha });
        }

        return JsonSerializer.Serialize(new
        {
            runId = id,
            recipe = Str(run.Value, "recipe"),
            startedAt = Str(run.Value, "startedAt"),
            endedAt = Str(run.Value, "endedAt"),
            outcome = Str(run.Value, "outcome"),
            message = Str(run.Value, "message"),
            directory = dir,
            files,
            notAvailable,
        }, Pretty);
    }

    private async Task<(long Bytes, string Sha256)> DownloadAsync(string runId, string kind, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long offset = 0;
        while (true)
        {
            var chunk = await CallRaw("runs.file", new { runId, kind, offset, maxBytes = ChunkBytes });
            byte[] data = Convert.FromBase64String(chunk.GetProperty("contentBase64").GetString() ?? "");
            fs.Write(data);
            sha.AppendData(data);
            offset += data.Length;
            if (chunk.GetProperty("eof").GetBoolean()) break;
            if (data.Length == 0)
                throw new McpException($"runs.file returned an empty chunk before eof for {runId}/{kind} at offset {offset}");
        }
        return (offset, Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant());
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    [McpServerTool(Name = "validate_recipe"),
     Description("Validate recipe content WITHOUT saving. Returns line-numbered errors and warnings. " +
                 "Pass isRcpx=true for full .rcpx XML, false for bare DSL script text.")]
    public Task<string> ValidateRecipe(
        [Description("The .rcpx XML or bare DSL text.")] string content,
        [Description("true = content is .rcpx XML; false = bare DSL script.")] bool isRcpx = true) =>
        Call("recipe.validate", new { content, isRcpx });

    [McpServerTool(Name = "compute_deposition_plan"),
     Description("Deterministic dwell and thickness arithmetic for a two-material multilayer, computed on the " +
                 "machine; use it instead of calculating by hand. gamma is the heavy-layer fraction, " +
                 "heavy thickness / period_A. Returns exact and 2-decimal dwells, the period the rounded and " +
                 "the whole-second dwells would give, and optional adhesion-layer dwell and dead-time-corrected " +
                 "heavy thickness. Every value is planned from rate and dwell (measurementStatus), never " +
                 "measured: do not report it as a measured result. Invalid or non-positive inputs fail with " +
                 "bad_request. Touches no hardware.")]
    public Task<string> ComputeDepositionPlan(
        [Description("Target bilayer period in angstrom, > 0.")] double period_A,
        [Description("Heavy-layer fraction of the period, 0 < gamma < 1.")] double gamma,
        [Description("Heavy-material deposition rate in angstrom per second, > 0.")] double heavy_rate_A_per_s,
        [Description("Light-material deposition rate in angstrom per second, > 0.")] double light_rate_A_per_s,
        [Description("Number of bilayers, >= 1.")] int bilayer_count,
        [Description("Optional adhesion-layer thickness in angstrom, >= 0.")] double? adhesion_thickness_A = null,
        [Description("Adhesion-layer rate in angstrom per second; required and > 0 when adhesion_thickness_A > 0.")]
        double? adhesion_rate_A_per_s = null,
        [Description("Optional dead time in seconds at the start of a layer, >= 0.")] double? dead_time_s = null,
        [Description("Rate in angstrom per second the heavy layer grows at after the dead time; required and > 0 " +
                     "when dead_time_s is given.")] double? dead_time_rate_A_per_s = null) =>
        Call("recipe.compute_plan", new
        {
            period_A, gamma, heavy_rate_A_per_s, light_rate_A_per_s, bilayer_count,
            adhesion_thickness_A, adhesion_rate_A_per_s, dead_time_s, dead_time_rate_A_per_s,
        });

    [McpServerTool(Name = "upload_recipe"),
     Description("Validate then save a .rcpx recipe onto the machine. Fails with line-numbered errors when invalid; " +
                 "refuses to overwrite an existing recipe unless overwrite=true. Avoid `message` steps for " +
                 "unattended runs: they pop a blocking operator-acknowledge dialog on the Control UI that " +
                 "survives abort_recipe and can stall shutdown until a human dismisses it.")]
    public Task<string> UploadRecipe(
        [Description("Recipe name (filename without .rcpx).")] string name,
        [Description("Full .rcpx XML content.")] string content,
        [Description("Replace an existing recipe of the same name.")] bool overwrite = false) =>
        Call("recipe.upload", new { name, content, overwrite });

    [McpServerTool(Name = "start_recipe"),
     Description("Start a recipe by name (user .rcpx or embedded). All interlocks and the single-recipe guard " +
                 "stay in force. Returns 'accepted' — poll recipe_status / tail_log for actual progress and errors. " +
                 "If the recipe contains a `message` step, that step blocks on an operator-acknowledge dialog on " +
                 "the Control UI until a human dismisses it. " + InterlockRefusalRule)]
    public Task<string> StartRecipe(
        [Description("Recipe name as listed by describe_recipes.")] string name,
        [Description("Optional JSON object of parameter overrides, e.g. {\"time\":\"30\"}.")]
        string? parametersJson = null)
    {
        Dictionary<string, string>? pars = null;
        if (!string.IsNullOrWhiteSpace(parametersJson))
            pars = JsonSerializer.Deserialize<Dictionary<string, string>>(parametersJson)
                ?? throw new McpException("parametersJson must be a JSON object of string values");
        return Call("recipe.start", new { name, parameters = pars });
    }

    [McpServerTool(Name = "abort_recipe"),
     Description("Abort the running recipe. The machine runs its full FAILURE_PROTOCOL shutdown sequence " +
                 "(sources off, safe state) — this takes on the order of 30 seconds, not instant, so poll " +
                 "recipe_status with a generous timeout (60+ seconds recommended) rather than assuming it has " +
                 "stopped right away.")]
    public Task<string> AbortRecipe() => Call("recipe.abort");

    [McpServerTool(Name = "resolve_pause"),
     Description("Answer a fault pause. When a recoverable step fails (a supply that did not respond, a " +
                 "pwait/swait timeout), the run does NOT tear the machine down: it soft-holds — drives " +
                 "stopped, the profile's FAILURE_HOLD run, the failed supply zeroed — reports state Paused " +
                 "via recipe_status, and waits for this decision or its watchdog (default 600s, then abort). " +
                 "'retry' re-runs the failed step (a deposit resumes only its REMAINING dwell); 'skip' records " +
                 "it and continues — dangerous on a power/gas/valve step, since later lines assume it " +
                 "succeeded; 'abort' runs the full FAILURE_PROTOCOL. Fails with 'no fault pause is waiting " +
                 "for a decision' when nothing is paused. Read recipe_status first: its stepDescription names " +
                 "the failed step and the driver's reason, and the hold report says what was actually held — " +
                 "a hold is not a guarantee (a profile with no FAILURE_HOLD closes nothing). After 'retry' the " +
                 "pause can REOPEN within seconds, so re-poll rather than assuming the run resumed.")]
    public Task<string> ResolvePause(
        [Description("retry | skip | abort")] string decision) =>
        Call("recipe.resolve_pause", new { decision });

    [McpServerTool(Name = "device_command"),
     Description("Direct manual device command, same path as the operator panels — interlocks enforced on the " +
                 "hardware thread. Returns 'accepted': verify via device_snapshot and tail_log (interlock denials " +
                 "appear there naming the blocking device). Rejected while a recipe is running. " +
                 "PSU rule: set the level first, then switch output on (two commands). " +
                 "ARGS SHAPE DEPENDS ON deviceType: for Valve/MFC/Pump/Stage, argsJson is just the raw value(s) " +
                 "the manual panel sends — e.g. [true] to open a valve, [5.0] for an MFC flow setpoint, " +
                 "[position] to move a stage. For Power/Drive/Controller, argsJson leads with a command-name " +
                 "string followed by its values. These are the PANEL command names, NOT the recipe DSL verbs: " +
                 "for a PSU use [\"SetCurrent\", 250] / [\"SetVoltage\", v] / [\"SetPower\", w] / " +
                 "[\"SetFrequency\", f] / [\"SetDutyCycle\", d] / [\"OutputOn\"] / [\"OutputOff\"] / " +
                 "[\"ClearFault\"] / [\"Reconnect\"] — energise with SetCurrent (or SetPower) THEN OutputOn, " +
                 "two commands. Do NOT send the DSL verbs \"set\"/\"power\" here; they are not panel commands " +
                 "and are rejected. Use [\"home\"] to home a drive. Never prefix a Valve/MFC/Pump/Stage " +
                 "command with a command-name token; it is not expected there. " + InterlockRefusalRule)]
    public Task<string> DeviceCommand(
        [Description("Device kind: Valve, Pump, MFC, Stage, Power, Drive, Controller.")] string deviceType,
        [Description("Device id from describe_hardware.")] string deviceId,
        [Description("JSON array of command args — shape depends on deviceType, see tool description. " +
                     "e.g. [true] to open a valve, [\"SetCurrent\", 250] then [\"OutputOn\"] for a PSU.")]
        string argsJson)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argsJson);
        if (args.ValueKind != JsonValueKind.Array)
            throw new McpException("argsJson must be a JSON array");
        return Call("device.command", new { type = deviceType, device = deviceId, args });
    }
}
