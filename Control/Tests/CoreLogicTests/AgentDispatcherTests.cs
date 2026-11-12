using System.Text.Json;
using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentDispatcherTests
{
    private abstract class FakeSysBase : IAgentSystemAccess
    {
        public List<string> Calls = [];
        public (string Type, string Id, object[] Args)? LastDevice;
        public (string Name, IReadOnlyDictionary<string, string>? Pars)? LastStart;

        public virtual object DescribeMachine() { Calls.Add("machine"); return new { profile = "TEST" }; }
        public virtual object DescribeHardware() { Calls.Add("hardware"); return new { }; }
        public virtual object DescribeRecipes(string? folder)
        { Calls.Add(folder is null ? "recipes" : $"recipes:{folder}"); return new { }; }
        public virtual object DescribeDsl() { Calls.Add("dsl"); return new { }; }
        public virtual object DescribeInterlocks() { Calls.Add("interlocks"); return new { }; }
        public virtual object DescribeAlarms() { Calls.Add("alarms"); return new { }; }
        public virtual object SystemStatus() { Calls.Add("status"); return new { }; }
        public virtual object DeviceSnapshot() { Calls.Add("devices"); return new { }; }
        public virtual object TailLog(int count) { Calls.Add($"tail:{count}"); return Array.Empty<object>(); }
        public virtual object RecipeStatus() { Calls.Add("rstatus"); return new { }; }
        public (string RunId, string Kind, long Offset, int MaxBytes)? LastRead;
        public virtual object ListRuns(int count) { Calls.Add($"runs:{count}"); return new { runs = Array.Empty<object>() }; }
        public virtual object ReadRunFile(string runId, string kind, long offset, int maxBytes)
        { Calls.Add("runfile"); LastRead = (runId, kind, offset, maxBytes); return new { }; }
        public virtual IReadOnlyList<RecipeError> ValidateRecipe(string content, bool isRcpx)
        { Calls.Add("validate"); return [new RecipeError(2, 1, "bad thing")]; }
        public virtual object SaveRecipe(string name, string content, bool overwrite, string? folder)
        {
            Calls.Add(folder is null ? $"save:{name}:{overwrite}" : $"save:{name}:{overwrite}:{folder}");
            return new { saved = name };
        }
        public virtual void StartRecipe(string name, IReadOnlyDictionary<string, string>? p)
        { Calls.Add("start"); LastStart = (name, p); }
        public virtual void AbortRecipe() => Calls.Add("abort");
        public virtual void ResolveFaultPause(APS.SystemControl.Core.FaultDecision d) =>
            Calls.Add($"resolve:{d}");
        public virtual void SendDeviceCommand(string t, string id, object[] a)
        { Calls.Add("device"); LastDevice = (t, id, a); }
    }

    private sealed class FakeSys : FakeSysBase
    {
    }

    private const string SecretHash = "2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b";
    private static AgentServerConfig Cfg(bool manual = true) => new()
    {
        Enabled = true, AllowRecipe = true, AllowManual = manual,
        Keys = [new AgentKey("dev", SecretHash, AgentTier.Manual)]
    };

    private static JsonElement Root(string line) => JsonDocument.Parse(line).RootElement;

    [Fact]
    public void Auth_success_returns_tier_key_name_and_identity()
    {
        var d = new AgentDispatcher(new FakeSys());
        string resp = d.HandleAuth("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""",
            Cfg(), out var tier, out var name);
        Assert.Equal(AgentTier.Manual, tier);
        Assert.Equal("dev", name);
        var root = Root(resp);
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Equal("manual", root.GetProperty("data").GetProperty("tier").GetString());
        Assert.Equal("TEST", root.GetProperty("data").GetProperty("machine").GetProperty("profile").GetString());
    }

    [Fact]
    public void Auth_failure_wrong_key_and_wrong_first_command()
    {
        var d = new AgentDispatcher(new FakeSys());
        string bad = d.HandleAuth("""{"v":1,"id":0,"cmd":"auth","args":{"key":"nope"}}""",
            Cfg(), out var tier, out _);
        Assert.Equal(AgentTier.None, tier);
        Assert.Equal("auth_failed", Root(bad).GetProperty("error").GetProperty("code").GetString());

        string notAuth = d.HandleAuth("""{"v":1,"id":0,"cmd":"status.system"}""",
            Cfg(), out tier, out _);
        Assert.Equal(AgentTier.None, tier);
        Assert.Equal("auth_failed", Root(notAuth).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Auth_with_unsupported_version_is_rejected()
    {
        var d = new AgentDispatcher(new FakeSys());
        string resp = d.HandleAuth("""{"v":2,"cmd":"auth","args":{"key":"secret"}}""",
            Cfg(), out var tier, out _);
        Assert.Equal(AgentTier.None, tier);
        Assert.False(Root(resp).GetProperty("ok").GetBoolean());
        Assert.Equal("auth_failed", Root(resp).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Auth_hands_back_the_key_recipe_folder()
    {
        var cfg = new AgentServerConfig
        {
            Enabled = true, AllowRecipe = true,
            Keys = [new AgentKey("agent", SecretHash, AgentTier.Recipe, "Agent-Paper2"),
                    new AgentKey("plain", "00", AgentTier.Recipe)],
        };
        var d = new AgentDispatcher(new FakeSys());
        d.HandleAuth("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""",
            cfg, out var tier, out var name, out var folder);
        Assert.Equal(AgentTier.Recipe, tier);
        Assert.Equal("agent", name);
        Assert.Equal("Agent-Paper2", folder);

        d.HandleAuth("""{"v":1,"id":0,"cmd":"auth","args":{"key":"nope"}}""",
            cfg, out tier, out _, out folder);
        Assert.Equal(AgentTier.None, tier);
        Assert.Null(folder);
    }

    [Fact]
    public void Upload_and_describe_recipes_carry_the_session_folder()
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);
        d.Handle("""{"v":1,"id":1,"cmd":"recipe.upload","args":{"name":"P2-01","content":"y"}}""",
            AgentTier.Recipe, "agent", "Agent-Paper2");
        d.Handle("""{"v":1,"id":2,"cmd":"describe.recipes"}""", AgentTier.Recipe, "agent", "Agent-Paper2");
        d.Handle("""{"v":1,"id":3,"cmd":"recipe.upload","args":{"name":"Lab","content":"y"}}""",
            AgentTier.Recipe, "dev");
        d.Handle("""{"v":1,"id":4,"cmd":"describe.recipes"}""", AgentTier.Recipe, "dev");
        Assert.Equal(["save:P2-01:False:Agent-Paper2", "recipes:Agent-Paper2", "save:Lab:False", "recipes"], sys.Calls);
    }

    [Theory]
    [InlineData("status.system", AgentTier.Read, true)]
    [InlineData("runs.list", AgentTier.Read, true)]
    [InlineData("runs.file", AgentTier.Read, true)]
    [InlineData("recipe.start", AgentTier.Read, false)]
    [InlineData("recipe.start", AgentTier.Recipe, true)]
    [InlineData("device.command", AgentTier.Recipe, false)]
    [InlineData("device.command", AgentTier.Manual, true)]
    public void Tier_gating_matrix(string cmd, AgentTier granted, bool allowed)
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);
        string args = cmd switch
        {
            "recipe.start" => ""","args":{"name":"Cleaning"}""",
            "device.command" => ""","args":{"type":"Valve","device":"A1","args":[true]}""",
            "runs.file" => ""","args":{"runId":"2026-09-03_10-51-51_X","kind":"tsv"}""",
            _ => "",
        };
        string resp = d.Handle($$"""{"v":1,"id":5,"cmd":"{{cmd}}"{{args}}}""", granted, "dev");
        Assert.Equal(allowed, Root(resp).GetProperty("ok").GetBoolean());
        if (!allowed)
            Assert.Equal("tier_denied", Root(resp).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Unknown_command_and_bad_json_map_to_errors()
    {
        var d = new AgentDispatcher(new FakeSys());
        Assert.Equal("unknown_command",
            Root(d.Handle("""{"v":1,"id":1,"cmd":"selfdestruct"}""", AgentTier.Manual, "dev"))
                .GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("bad_request",
            Root(d.Handle("garbage", AgentTier.Manual, "dev"))
                .GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Device_command_converts_json_args_to_clr_values()
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);
        d.Handle("""{"v":1,"id":2,"cmd":"device.command","args":{"type":"MFC","device":"Ar1","args":["set",5.5,3,true]}}""",
            AgentTier.Manual, "dev");
        Assert.NotNull(sys.LastDevice);
        var a = sys.LastDevice!.Value.Args;
        Assert.Equal("set", a[0]);
        Assert.Equal(5.5f, Assert.IsType<float>(a[1]));
        Assert.Equal(3, Assert.IsType<int>(a[2]));
        Assert.Equal(true, a[3]);
    }

    [Fact]
    public void AgentOpException_maps_to_error_payload()
    {
        var sys = new ThrowingSys();
        var d = new AgentDispatcher(sys);
        var root = Root(d.Handle("""{"v":1,"id":3,"cmd":"recipe.start","args":{"name":"X"}}""",
            AgentTier.Recipe, "dev"));
        Assert.Equal("busy", root.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class ThrowingSys : FakeSysBase
    {
        public override void StartRecipe(string n, IReadOnlyDictionary<string, string>? p) =>
            throw new AgentOpException(AgentErrors.Busy, "A recipe is already running.");
    }

    [Fact]
    public void Runs_commands_route_their_arguments_with_defaults()
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);

        Assert.True(Root(d.Handle("""{"v":1,"id":1,"cmd":"runs.list"}""", AgentTier.Read, "dev"))
            .GetProperty("ok").GetBoolean());
        Assert.Contains("runs:5", sys.Calls);                         // default count

        d.Handle("""{"v":1,"id":2,"cmd":"runs.list","args":{"count":12}}""", AgentTier.Read, "dev");
        Assert.Contains("runs:12", sys.Calls);

        d.Handle("""{"v":1,"id":3,"cmd":"runs.file","args":{"runId":"2026-09-03_10-51-51_X","kind":"log"}}""",
            AgentTier.Read, "dev");
        Assert.Equal(("2026-09-03_10-51-51_X", "log", 0L, 1 << 20), sys.LastRead);

        d.Handle("""{"v":1,"id":4,"cmd":"runs.file","args":{"runId":"r","kind":"tsv","offset":1048576,"maxBytes":4096}}""",
            AgentTier.Read, "dev");
        Assert.Equal(("r", "tsv", 1048576L, 4096), sys.LastRead);

        var missing = Root(d.Handle("""{"v":1,"id":5,"cmd":"runs.file","args":{"kind":"tsv"}}""", AgentTier.Read, "dev"));
        Assert.Equal("bad_request", missing.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("retry", APS.SystemControl.Core.FaultDecision.Retry)]
    [InlineData("SKIP", APS.SystemControl.Core.FaultDecision.Skip)]
    [InlineData("Abort", APS.SystemControl.Core.FaultDecision.Abort)]
    [InlineData("hold", APS.SystemControl.Core.FaultDecision.Hold)]
    public void Resolve_pause_routes_each_decision_case_insensitively(
        string wire, APS.SystemControl.Core.FaultDecision expected)
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);
        var root = Root(d.Handle(
            $$$"""{"v":1,"id":1,"cmd":"recipe.resolve_pause","args":{"decision":"{{{wire}}}"}}""",
            AgentTier.Recipe, "dev"));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.True(root.GetProperty("data").GetProperty("accepted").GetBoolean());
        Assert.Contains($"resolve:{expected}", sys.Calls);
    }

    [Fact]
    public void Resolve_pause_rejects_an_unknown_decision_and_a_missing_one()
    {
        var sys = new FakeSys();
        var d = new AgentDispatcher(sys);

        var bad = Root(d.Handle(
            """{"v":1,"id":1,"cmd":"recipe.resolve_pause","args":{"decision":"continue"}}""",
            AgentTier.Recipe, "dev")).GetProperty("error");
        Assert.Equal("bad_request", bad.GetProperty("code").GetString());
        Assert.Contains("retry, skip, abort", bad.GetProperty("message").GetString());

        var missing = Root(d.Handle("""{"v":1,"id":2,"cmd":"recipe.resolve_pause"}""",
            AgentTier.Recipe, "dev")).GetProperty("error");
        Assert.Equal("bad_request", missing.GetProperty("code").GetString());

        Assert.Empty(sys.Calls);
    }

    /// <summary>The no-pause-waiting error is the reason the command exists as more than a
    /// pass-through: Core.ResolveFault is a no-op when nothing is pending, so without this the
    /// wire would report success for a pause that never happened.</summary>
    [Fact]
    public void Resolve_pause_with_nothing_paused_is_an_error_not_a_silent_success()
    {
        var root = Root(new AgentDispatcher(new NoPausePendingSys()).Handle(
            """{"v":1,"id":1,"cmd":"recipe.resolve_pause","args":{"decision":"retry"}}""",
            AgentTier.Recipe, "dev"));
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("not_found", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("no fault pause is waiting for a decision",
            root.GetProperty("error").GetProperty("message").GetString());
    }

    private sealed class NoPausePendingSys : FakeSysBase
    {
        public override void ResolveFaultPause(APS.SystemControl.Core.FaultDecision d) =>
            throw new AgentOpException(AgentErrors.NotFound, "no fault pause is waiting for a decision");
    }

    [Fact]
    public void Resolve_pause_needs_the_recipe_tier()
    {
        var d = new AgentDispatcher(new FakeSys());
        const string Line = """{"v":1,"id":1,"cmd":"recipe.resolve_pause","args":{"decision":"retry"}}""";
        Assert.Equal("tier_denied",
            Root(d.Handle(Line, AgentTier.Read, "dev")).GetProperty("error").GetProperty("code").GetString());
        Assert.True(Root(d.Handle(Line, AgentTier.Recipe, "dev")).GetProperty("ok").GetBoolean());
    }

    [Fact]
    public void Mutating_commands_invoke_audit_with_key_name()
    {
        var audits = new List<string>();
        var d = new AgentDispatcher(new FakeSys(), audits.Add);
        d.Handle("""{"v":1,"id":1,"cmd":"status.system"}""", AgentTier.Manual, "dev");
        d.Handle("""{"v":1,"id":2,"cmd":"recipe.abort"}""", AgentTier.Manual, "dev");
        d.Handle("""{"v":1,"id":3,"cmd":"device.command","args":{"type":"Valve","device":"A1","args":[true]}}""",
            AgentTier.Manual, "dev");
        d.Handle("""{"v":1,"id":4,"cmd":"recipe.resolve_pause","args":{"decision":"skip"}}""",
            AgentTier.Manual, "dev");
        Assert.Equal(3, audits.Count);                    // reads are not audited
        Assert.Contains(audits, a => a.Contains("recipe.resolve_pause") && a.Contains("skip"));
        Assert.All(audits, a => Assert.Contains("[dev]", a));
    }

    private static AgentDispatcher WithMode(FakeSys sys, APS.SystemControl.Core.ControlMode mode)
    {
        var svc = new APS.SystemControl.Core.ControlModeService();
        svc.Initialize(new APS.SystemControl.Core.ControlModeStartup(null, false, mode.ToString(), null));
        return new AgentDispatcher(sys, null, cls => svc.Authorize(APS.SystemControl.Core.CommandOrigin.Host, cls));
    }

    [Theory]
    [InlineData("""{"v":1,"id":7,"cmd":"recipe.start","args":{"name":"Cleaning"}}""")]
    [InlineData("""{"v":1,"id":7,"cmd":"recipe.abort"}""")]
    [InlineData("""{"v":1,"id":7,"cmd":"recipe.resolve_pause","args":{"decision":"retry"}}""")]
    [InlineData("""{"v":1,"id":7,"cmd":"recipe.upload","args":{"name":"x","content":"y"}}""")]
    [InlineData("""{"v":1,"id":7,"cmd":"device.command","args":{"type":"Valve","device":"V1","args":[true]}}""")]
    public void Mutating_commands_are_refused_in_Local_with_control_mode_code(string line)
    {
        var sys = new FakeSys();
        var root = Root(WithMode(sys, APS.SystemControl.Core.ControlMode.Local).Handle(line, AgentTier.Manual, "dev"));
        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("control_mode", root.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("control mode is Local — host commands need Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the operator", root.GetProperty("error").GetProperty("message").GetString());
        Assert.Empty(sys.Calls);
    }

    [Fact]
    public void Mutating_commands_pass_in_Remote_and_reads_pass_in_every_mode()
    {
        foreach (var mode in new[] { APS.SystemControl.Core.ControlMode.Offline, APS.SystemControl.Core.ControlMode.Local, APS.SystemControl.Core.ControlMode.Remote })
        {
            var sys = new FakeSys();
            var d = WithMode(sys, mode);
            Assert.True(Root(d.Handle("""{"v":1,"id":1,"cmd":"status.system"}""", AgentTier.Read, "r")).GetProperty("ok").GetBoolean());
            Assert.True(Root(d.Handle("""{"v":1,"id":2,"cmd":"recipe.validate","args":{"content":"x"}}""", AgentTier.Recipe, "r")).GetProperty("ok").GetBoolean());
        }
        var remote = new FakeSys();
        var ok = Root(WithMode(remote, APS.SystemControl.Core.ControlMode.Remote)
            .Handle("""{"v":1,"id":3,"cmd":"recipe.start","args":{"name":"Cleaning"}}""", AgentTier.Recipe, "r"));
        Assert.True(ok.GetProperty("ok").GetBoolean());
        Assert.Contains("start", remote.Calls);
    }

    [Fact]
    public void Tier_check_precedes_the_mode_check()
    {
        var root = Root(WithMode(new FakeSys(), APS.SystemControl.Core.ControlMode.Local)
            .Handle("""{"v":1,"id":4,"cmd":"device.command","args":{"type":"Valve","device":"V1","args":[true]}}""", AgentTier.Read, "r"));
        Assert.Equal("tier_denied", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Without_an_authorize_delegate_nothing_is_gated()
    {
        var sys = new FakeSys();
        var root = Root(new AgentDispatcher(sys).Handle("""{"v":1,"id":5,"cmd":"recipe.abort"}""", AgentTier.Recipe, "r"));
        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Contains("abort", sys.Calls);
    }

    [Fact]
    public void Audit_names_the_actor_after_the_key_when_present()
    {
        var audits = new List<string>();
        var d = new AgentDispatcher(new FakeSys(), audits.Add);
        d.Handle("""{"v":1,"id":1,"cmd":"recipe.abort","actor":"alice"}""", AgentTier.Recipe, "lab");
        d.Handle("""{"v":1,"id":2,"cmd":"recipe.abort"}""", AgentTier.Recipe, "lab");
        Assert.Equal(2, audits.Count);
        Assert.StartsWith("[lab/alice] recipe.abort", audits[0]);
        Assert.StartsWith("[lab] recipe.abort", audits[1]);
    }

    private sealed class ModeFakeSys(string controlMode) : FakeSysBase
    {
        public override object DescribeMachine() =>
            new { profile = "TEST", chambers = new[] { "Main" }, controlMode };
    }

    [Theory]
    [InlineData(AgentTier.Read, "Remote", false, 0)]
    [InlineData(AgentTier.Recipe, "Remote", true, 4)]
    [InlineData(AgentTier.Recipe, "Local", false, 4)]
    [InlineData(AgentTier.Manual, "Remote", true, 5)]
    [InlineData(AgentTier.Manual, "Offline", false, 5)]
    public void Describe_machine_includes_session_tier_and_mutating_authority(
        AgentTier tier, string controlMode, bool expectedCanMutate, int expectedToolCount)
    {
        var d = new AgentDispatcher(new ModeFakeSys(controlMode));
        var data = Root(d.Handle("""{"v":1,"id":1,"cmd":"describe.machine"}""", tier, "key1"))
            .GetProperty("data");

        Assert.Equal(tier.ToString(), data.GetProperty("sessionTier").GetString());
        Assert.Equal(expectedCanMutate, data.GetProperty("canExecuteMutatingNow").GetBoolean());
        Assert.Equal(expectedToolCount, data.GetProperty("allowedMutatingTools").GetArrayLength());
        Assert.Equal("TEST", data.GetProperty("profile").GetString());
        Assert.Equal(controlMode, data.GetProperty("controlMode").GetString());
    }

    [Fact]
    public void Mutating_tool_lists_are_cumulative_by_tier()
    {
        string[] Tools(AgentTier tier) => Root(new AgentDispatcher(new ModeFakeSys("Remote"))
                .Handle("""{"v":1,"id":1,"cmd":"describe.machine"}""", tier, "k"))
            .GetProperty("data").GetProperty("allowedMutatingTools")
            .EnumerateArray().Select(e => e.GetString()!).ToArray();

        Assert.Empty(Tools(AgentTier.Read));
        Assert.Equal(["upload_recipe", "start_recipe", "abort_recipe", "resolve_pause"], Tools(AgentTier.Recipe));
        Assert.Equal(["upload_recipe", "start_recipe", "abort_recipe", "resolve_pause", "device_command"],
            Tools(AgentTier.Manual));
    }

    [Fact]
    public void Describe_machine_without_a_control_mode_cannot_mutate_now()
    {
        var data = Root(new AgentDispatcher(new FakeSys())
                .Handle("""{"v":1,"id":1,"cmd":"describe.machine"}""", AgentTier.Manual, "k"))
            .GetProperty("data");

        Assert.False(data.GetProperty("canExecuteMutatingNow").GetBoolean());
        Assert.Equal("TEST", data.GetProperty("profile").GetString());
    }

    [Fact]
    public void Auth_reply_machine_carries_the_authority_fields_of_the_granted_tier()
    {
        var d = new AgentDispatcher(new ModeFakeSys("Remote"));
        var machine = Root(d.HandleAuth("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""",
            Cfg(), out _, out _)).GetProperty("data").GetProperty("machine");

        Assert.Equal("Manual", machine.GetProperty("sessionTier").GetString());
        Assert.True(machine.GetProperty("canExecuteMutatingNow").GetBoolean());
    }

    private const string PlanBase =
        "\"period_A\":68.5,\"gamma\":0.215,\"heavy_rate_A_per_s\":0.4711,\"light_rate_A_per_s\":0.4030,\"bilayer_count\":5";

    private static JsonElement Plan(string args, AgentTier tier = AgentTier.Read) =>
        Root(new AgentDispatcher(new FakeSys()).Handle(
            "{\"v\":1,\"id\":1,\"cmd\":\"recipe.compute_plan\",\"args\":{" + args + "}}", tier, "k"));

    [Fact]
    public void Compute_plan_returns_dwells_and_the_integer_rounded_period()
    {
        var root = Plan(PlanBase + ",\"adhesion_thickness_A\":197.0,\"adhesion_rate_A_per_s\":0.4711," +
                        "\"dead_time_s\":2.02,\"dead_time_rate_A_per_s\":0.4990");

        Assert.True(root.GetProperty("ok").GetBoolean());
        var d = root.GetProperty("data");
        Assert.Equal("planned_from_rate_and_dwell_not_measured", d.GetProperty("measurementStatus").GetString());
        Assert.Equal(68.5, d.GetProperty("period_A").GetDouble());
        Assert.Equal(0.215, d.GetProperty("gamma").GetDouble());
        Assert.Equal(5, d.GetProperty("bilayer_count").GetInt32());
        Assert.Equal(14.7275, d.GetProperty("heavy_thickness_A").GetDouble(), 6);
        Assert.Equal(53.7725, d.GetProperty("light_thickness_A").GetDouble(), 6);
        Assert.Equal(31.26194, d.GetProperty("heavy_dwell_exact_s").GetDouble(), 5);
        Assert.Equal(133.430521, d.GetProperty("light_dwell_exact_s").GetDouble(), 6);
        Assert.Equal(31.26, d.GetProperty("heavy_dwell_s").GetDouble());
        Assert.Equal(133.43, d.GetProperty("light_dwell_s").GetDouble());
        Assert.Equal(68.498876, d.GetProperty("period_from_rounded_dwells_A").GetDouble(), 6);
        var ints = d.GetProperty("integer_rounded_check");
        Assert.Equal(31, ints.GetProperty("heavy_dwell_int_s").GetInt32());
        Assert.Equal(133, ints.GetProperty("light_dwell_int_s").GetInt32());
        Assert.Equal(68.20, ints.GetProperty("period_from_integer_dwells_A").GetDouble());
        Assert.Equal(418.17, d.GetProperty("adhesion_dwell_s").GetDouble());
        Assert.Equal(14.59, d.GetProperty("dead_time_heavy_thickness_A").GetDouble());
    }

    [Fact]
    public void Compute_plan_without_optional_inputs_reports_them_as_null()
    {
        var d = Plan(PlanBase).GetProperty("data");

        Assert.Equal(JsonValueKind.Null, d.GetProperty("adhesion_dwell_s").ValueKind);
        Assert.Equal(JsonValueKind.Null, d.GetProperty("dead_time_heavy_thickness_A").ValueKind);
    }

    [Fact]
    public void Compute_plan_dead_time_longer_than_the_dwell_deposits_nothing()
    {
        var d = Plan(PlanBase + ",\"dead_time_s\":60,\"dead_time_rate_A_per_s\":0.499").GetProperty("data");

        Assert.Equal(0.0, d.GetProperty("dead_time_heavy_thickness_A").GetDouble());
    }

    [Fact]
    public void Compute_plan_needs_only_the_read_tier_and_commands_no_device()
    {
        var sys = new FakeSys();
        var root = Root(new AgentDispatcher(sys).Handle(
            "{\"v\":1,\"id\":1,\"cmd\":\"recipe.compute_plan\",\"args\":{" + PlanBase + "}}", AgentTier.Read, "k"));

        Assert.True(root.GetProperty("ok").GetBoolean());
        Assert.Empty(sys.Calls);
    }

    [Theory]
    [InlineData("\"period_A\":-5,\"gamma\":0.215,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":0,\"gamma\":0.215,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":1,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":1.5,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":-0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":0")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":2.5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4")]
    [InlineData("\"period_A\":\"68.5\",\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":1e999,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    [InlineData("\"period_A\":68.5,\"gamma\":0.2,\"heavy_rate_A_per_s\":1e-320,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5")]
    public void Compute_plan_refuses_unusable_inputs_with_bad_request(string args)
    {
        var root = Plan(args);

        Assert.False(root.GetProperty("ok").GetBoolean());
        Assert.Equal("bad_request", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(",\"adhesion_thickness_A\":197.0")]
    [InlineData(",\"adhesion_thickness_A\":197.0,\"adhesion_rate_A_per_s\":0")]
    [InlineData(",\"adhesion_thickness_A\":-1,\"adhesion_rate_A_per_s\":0.47")]
    [InlineData(",\"dead_time_s\":2.02")]
    [InlineData(",\"dead_time_s\":2.02,\"dead_time_rate_A_per_s\":-0.4")]
    [InlineData(",\"dead_time_s\":-1,\"dead_time_rate_A_per_s\":0.4")]
    [InlineData(",\"dead_time_rate_A_per_s\":0")]
    public void Compute_plan_refuses_inconsistent_optional_inputs(string optional)
    {
        var root = Plan(PlanBase + optional);

        Assert.Equal("bad_request", root.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public void Compute_plan_error_names_the_offending_argument()
    {
        var root = Plan("\"period_A\":-5,\"gamma\":0.2,\"heavy_rate_A_per_s\":0.47,\"light_rate_A_per_s\":0.4,\"bilayer_count\":5");

        Assert.Contains("period_A", root.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void Compute_plan_adhesion_thickness_of_zero_needs_no_rate_and_takes_no_time()
    {
        var d = Plan(PlanBase + ",\"adhesion_thickness_A\":0").GetProperty("data");

        Assert.Equal(0.0, d.GetProperty("adhesion_dwell_s").GetDouble());
    }
}
