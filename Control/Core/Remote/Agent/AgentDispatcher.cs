using System.Globalization;
using System.Text.Json;

namespace APS.SystemControl.Core.Remote.Agent;

internal sealed class AgentDispatcher(IAgentSystemAccess sys, Action<string>? audit = null,
    Func<CommandClass, string?>? authorize = null)
{
    /// <summary>Optional wire capabilities advertised in the auth reply (e.g. "bus"). Protocol
    /// version stays 1 — the shipped bridge checks V with strict equality.</summary>
    public IReadOnlyList<string> Capabilities { get; init; } = [];

    public string HandleAuth(string line, AgentServerConfig cfg, out AgentTier tier, out string keyName) =>
        HandleAuth(line, cfg, out tier, out keyName, out _);

    /// <summary>As above, plus the key's recipe folder: null when the key is unscoped or
    /// authentication failed. The session passes it to <see cref="Handle"/>.</summary>
    public string HandleAuth(string line, AgentServerConfig cfg, out AgentTier tier, out string keyName,
        out string? recipeFolder)
    {
        tier = AgentTier.None;
        keyName = "";
        recipeFolder = null;
        if (!AgentProtocol.TryParseRequest(line, out var req) || req.Cmd != "auth")
            return AgentProtocol.FormatError(0, AgentErrors.AuthFailed, "first request must be 'auth'");
        if (req.V != AgentProtocol.Version)
            return AgentProtocol.FormatError(req.Id, AgentErrors.AuthFailed,
                $"unsupported protocol version {req.V}; server speaks {AgentProtocol.Version}");
        string? key = req.Args?.TryGetProperty("key", out var k) == true ? k.GetString() : null;
        var granted = key is null ? null : cfg.AuthenticateKey(key);
        if (granted is null)
            return AgentProtocol.FormatError(req.Id, AgentErrors.AuthFailed, "unknown API key");
        (tier, keyName, recipeFolder) = (granted.Tier, granted.Name, granted.RecipeFolder);
        object machine;
        try { machine = EnrichMachine(sys.DescribeMachine(), tier); } catch { machine = new { }; }
        return AgentProtocol.FormatOk(req.Id, new
        {
            tier = tier.ToString().ToLowerInvariant(),
            keyName,
            protocol = AgentProtocol.Version,
            capabilities = Capabilities,
            machine,
        });
    }

    private static AgentTier RequiredTier(string cmd) => cmd switch
    {
        "describe.machine" or "describe.hardware" or "describe.recipes" or "describe.dsl"
            or "describe.interlocks" or "describe.alarms"
            or "status.system" or "status.devices" or "logs.tail" or "recipe.status"
            or "runs.list" or "runs.file" or "recipe.compute_plan" or "recipe.validate" => AgentTier.Read,
        // recipe.resolve_pause sits with start/abort, NOT at Manual: answering a fault pause
        // resumes or ends a run the same key was allowed to start in the first place, so it is
        // the same class of authority. It commands no device directly.
        "recipe.upload" or "recipe.start" or "recipe.abort"
            or "recipe.resolve_pause" => AgentTier.Recipe,
        "device.command" => AgentTier.Manual,
        _ => AgentTier.None,
    };

    private static bool IsMutating(string cmd) =>
        cmd is "recipe.upload" or "recipe.start" or "recipe.abort" or "recipe.resolve_pause"
            or "device.command";

    /// <param name="recipeFolder">The session key's recipe folder (from <see cref="HandleAuth(string, AgentServerConfig, out AgentTier, out string, out string?)"/>);
    /// null = unscoped. Confines recipe upload and listing.</param>
    public string Handle(string line, AgentTier tier, string keyName, string? recipeFolder = null)
    {
        if (!AgentProtocol.TryParseRequest(line, out var req))
            return AgentProtocol.FormatError(-1, AgentErrors.BadRequest, "unparseable request line");
        var need = RequiredTier(req.Cmd);
        if (need == AgentTier.None)
            return AgentProtocol.FormatError(req.Id, AgentErrors.UnknownCommand, $"unknown command '{req.Cmd}'");
        if (tier < need)
            return AgentProtocol.FormatError(req.Id, AgentErrors.TierDenied,
                $"'{req.Cmd}' requires the {need} tier; this key grants {tier}");
        // Control mode (spec 2026-09-06 §3/§4): a host may mutate only in Online-Remote. Checked after
        // the tier so a key that could never run the command still gets tier_denied.
        if (authorize is not null)
        {
            string? deny = authorize(IsMutating(req.Cmd) ? CommandClass.Mutate : CommandClass.Read);
            if (deny is not null)
                return AgentProtocol.FormatError(req.Id, AgentErrors.ControlMode, deny);
        }
        try
        {
            object? data = Invoke(req, tier, recipeFolder);
            if (IsMutating(req.Cmd))
                audit?.Invoke($"[{(req.Actor is null ? keyName : $"{keyName}/{req.Actor}")}] {req.Cmd} {Summarize(req.Args)}");
            return AgentProtocol.FormatOk(req.Id, data);
        }
        catch (AgentOpException ex)
        {
            return AgentProtocol.FormatError(req.Id, ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex)
        {
            return AgentProtocol.FormatError(req.Id, AgentErrors.Internal, ex.Message);
        }
    }

    private static readonly string[] RecipeTools = ["upload_recipe", "start_recipe", "abort_recipe", "resolve_pause"];
    private static readonly string[] ManualTools = [.. RecipeTools, "device_command"];

    private static object EnrichMachine(object machine, AgentTier tier)
    {
        var el = JsonSerializer.SerializeToElement(machine, AgentProtocol.Opt);
        var merged = new Dictionary<string, object?>();
        string? controlMode = null;
        if (el.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in el.EnumerateObject()) merged[p.Name] = p.Value;
            if (el.TryGetProperty("controlMode", out var mode) && mode.ValueKind == JsonValueKind.String)
                controlMode = mode.GetString();
        }
        merged["sessionTier"] = tier.ToString();
        merged["allowedMutatingTools"] = tier switch
        {
            >= AgentTier.Manual => ManualTools,
            >= AgentTier.Recipe => RecipeTools,
            _ => Array.Empty<string>(),
        };
        merged["canExecuteMutatingNow"] = tier >= AgentTier.Recipe && controlMode == "Remote";
        return merged;
    }

    private object? Invoke(AgentRequest req, AgentTier tier, string? recipeFolder) => req.Cmd switch
    {
        "describe.machine" => EnrichMachine(sys.DescribeMachine(), tier),
        "describe.hardware" => sys.DescribeHardware(),
        "describe.recipes" => sys.DescribeRecipes(recipeFolder),
        "describe.dsl" => sys.DescribeDsl(),
        "describe.interlocks" => sys.DescribeInterlocks(),
        "describe.alarms" => sys.DescribeAlarms(),
        "status.system" => sys.SystemStatus(),
        "status.devices" => sys.DeviceSnapshot(),
        "logs.tail" => sys.TailLog(OptInt(req, "count", 50)),
        "recipe.status" => sys.RecipeStatus(),
        "runs.list" => sys.ListRuns(OptInt(req, "count", 5)),
        "runs.file" => sys.ReadRunFile(ReqString(req, "runId"), ReqString(req, "kind"),
            OptLong(req, "offset", 0), OptInt(req, "maxBytes", RunArtifacts.DefaultMaxBytes)),
        "recipe.validate" => sys.ValidateWithSummary(ReqString(req, "content"), OptBool(req, "isRcpx", true)).ToWire(),
        "recipe.upload" => sys.SaveRecipe(ReqString(req, "name"), ReqString(req, "content"), OptBool(req, "overwrite", false),
            recipeFolder),
        "recipe.start" => Do(() => sys.StartRecipe(ReqString(req, "name"), OptDict(req, "parameters"))),
        "recipe.abort" => Do(sys.AbortRecipe),
        "recipe.resolve_pause" => Do(() => sys.ResolveFaultPause(ReqDecision(req))),
        "device.command" => Do(() => sys.SendDeviceCommand(
            ReqString(req, "type"), ReqString(req, "device"), ToArgs(req))),
        "recipe.compute_plan" => DepositionPlanCalculator.Compute(ReadPlanRequest(req)),
        _ => throw new AgentOpException(AgentErrors.UnknownCommand, req.Cmd),
    };

    private static object Do(Action a) { a(); return new { accepted = true }; }

    /// <summary>The operator's answer to a fault pause, off the wire. Parsed HERE rather than in
    /// the system facade so a typo comes back as a <c>bad_request</c> naming the accepted values,
    /// and the facade only ever sees a decision the engine understands.</summary>
    private static FaultDecision ReqDecision(AgentRequest req)
    {
        string s = ReqString(req, "decision");
        return s.ToLowerInvariant() switch
        {
            "retry" => FaultDecision.Retry,
            "skip" => FaultDecision.Skip,
            "abort" => FaultDecision.Abort,
            "hold" => FaultDecision.Hold,
            _ => throw new AgentOpException(AgentErrors.BadRequest,
                $"unknown decision '{s}'; accepted values are retry, skip, abort, hold"),
        };
    }

    private static string ReqString(AgentRequest req, string name) =>
        req.Args?.TryGetProperty(name, out var v) == true && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new AgentOpException(AgentErrors.BadRequest, $"missing string arg '{name}'");

    private static int OptInt(AgentRequest req, string name, int fallback) =>
        req.Args?.TryGetProperty(name, out var v) == true && v.TryGetInt32(out int n) ? n : fallback;

    private static double? OptDouble(AgentRequest req, string name)
    {
        if (req.Args?.TryGetProperty(name, out var v) != true || v.ValueKind == JsonValueKind.Null) return null;
        if (v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out double d) || !double.IsFinite(d))
            throw new AgentOpException(AgentErrors.BadRequest, $"arg '{name}' must be a finite number");
        return d;
    }

    private static double ReqDouble(AgentRequest req, string name) =>
        OptDouble(req, name) ?? throw new AgentOpException(AgentErrors.BadRequest, $"missing numeric arg '{name}'");

    private static DepositionPlanRequest ReadPlanRequest(AgentRequest req) => new(
        ReqDouble(req, "period_A"),
        ReqDouble(req, "gamma"),
        ReqDouble(req, "heavy_rate_A_per_s"),
        ReqDouble(req, "light_rate_A_per_s"),
        req.Args?.TryGetProperty("bilayer_count", out var count) == true
            && count.ValueKind == JsonValueKind.Number && count.TryGetInt32(out int n)
            ? n
            : throw new AgentOpException(AgentErrors.BadRequest, "bilayer_count must be a whole number >= 1"),
        OptDouble(req, "adhesion_thickness_A"),
        OptDouble(req, "adhesion_rate_A_per_s"),
        OptDouble(req, "dead_time_s"),
        OptDouble(req, "dead_time_rate_A_per_s"));

    private static long OptLong(AgentRequest req, string name, long fallback) =>
        req.Args?.TryGetProperty(name, out var v) == true && v.TryGetInt64(out long n) ? n : fallback;

    private static bool OptBool(AgentRequest req, string name, bool fallback) =>
        req.Args?.TryGetProperty(name, out var v) == true
        && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static Dictionary<string, string>? OptDict(AgentRequest req, string name)
    {
        if (req.Args?.TryGetProperty(name, out var v) != true || v.ValueKind != JsonValueKind.Object) return null;
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in v.EnumerateObject()) d[p.Name] = p.Value.ToString();
        return d;
    }

    /// <summary>JSON array -> the CLR shapes ParamMsg args use: string / int / float / bool.</summary>
    private static object[] ToArgs(AgentRequest req)
    {
        if (req.Args?.TryGetProperty("args", out var arr) != true || arr.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<object>();
        foreach (var e in arr.EnumerateArray())
            list.Add(e.ValueKind switch
            {
                JsonValueKind.String => e.GetString()!,
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Number => e.TryGetInt32(out int n) ? n : (object)e.GetSingle(),
                _ => throw new AgentOpException(AgentErrors.BadRequest, $"unsupported arg kind {e.ValueKind}"),
            });
        return [.. list];
    }

    private static string Summarize(JsonElement? args) =>
        args is null ? "" : args.Value.GetRawText() is { Length: > 200 } s ? s[..200] + "…" : args.Value.GetRawText();
}
