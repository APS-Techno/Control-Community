using System.Text.Json;
using System.Threading.Channels;
using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Classes;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Remote.Agent;
using DeviceType = APS.Controls.Core.DeviceType;

namespace CoreLogicTests;

public class RecipeValidatorFactoryTests
{
    private const int CondMore = 1;

    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    private static BaseStage Stage(string id, bool connected, string? operational)
    {
        var stage = new BaseStage(new BaseDeviceParams(id, 0, "stage")) { Connected = connected };
        var el = new XElement("stage", id);
        if (operational is not null) el.SetAttributeValue("operational", operational);
        stage.ApplyConfig(el);
        return stage;
    }

    private static Alarms AdsMainAlarms()
    {
        var a = new Alarms("Main");
        a.Add("pm", 2e-1f, CondMore, SystemState.Processing, "high");
        a.Add("pm", 1e-4f, CondMore, SystemState.Idle, "high", delaySeconds: 60f);
        return a;
    }

    [Theory]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("False", false)]
    [InlineData("garbage", true)]
    [InlineData(null, true)]
    public void Stage_operational_attribute_is_parsed_with_default_true(string? attr, bool expected)
    {
        Assert.Equal(expected, Stage("rotation", true, attr).IsOperational);
    }

    [Fact]
    public void Stage_is_operational_by_default_without_ApplyConfig()
    {
        Assert.True(new BaseStage(new BaseDeviceParams("VS", 0, "stage")).IsOperational);
    }

    [Theory]
    [InlineData(true, "false", false)]
    [InlineData(true, null, true)]
    [InlineData(false, null, false)]
    [InlineData(false, "true", false)]
    public void Describe_stage_reports_connected_and_operational(bool connected, string? attr, bool expected)
    {
        var stage = Stage("rotation", connected, attr);

        var e = JsonDocument.Parse(JsonSerializer.Serialize(
            CoreAgentSystemAccess.DescribeDevice("rotation", stage), AgentProtocol.Opt)).RootElement;

        Assert.Equal(expected, e.GetProperty("isOperational").GetBoolean());
        Assert.Equal(connected, e.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public void Describe_controller_reports_its_chamber()
    {
        var ctrl = new BaseController(new BaseDeviceParams("TC1", 0, "ctrl")) { Chamber = "Main" };

        var e = JsonDocument.Parse(JsonSerializer.Serialize(
            CoreAgentSystemAccess.DescribeDevice("TC1", ctrl), AgentProtocol.Opt)).RootElement;

        Assert.Equal("Main", e.GetProperty("chamber").GetString());
    }

    [Fact]
    public void Chamber_limits_read_idle_and_processing_ceilings_with_delay()
    {
        var limits = APS.SystemControl.Core.Core.ChamberLimitsOf(
            new Dictionary<string, Alarms> { ["Main"] = AdsMainAlarms() });

        var main = limits["Main"];
        Assert.Equal(1e-4f, main.IdleCeilingTorr);
        Assert.Equal(60d, main.IdleDelaySeconds);
        Assert.Null(main.IdleUnless);
        Assert.Equal(2e-1f, main.ProcessingCeilingTorr);
    }

    [Fact]
    public void Chamber_without_pressure_rules_is_omitted()
    {
        var loadlock = new Alarms("Loadlock");
        loadlock.AddDeviceStatusAlarm("LL_TP", 5, SystemState.Idle, "pump");

        var limits = APS.SystemControl.Core.Core.ChamberLimitsOf(
            new Dictionary<string, Alarms> { ["Loadlock"] = loadlock });

        Assert.DoesNotContain("Loadlock", limits.Keys);
    }

    [Fact]
    public void Chamber_limits_keep_non_default_ceilings_and_the_guard_text()
    {
        var tm = new Alarms("TM");
        tm.Add("TM_pm", 1e-3f, CondMore, SystemState.Idle, "high");
        var ll = new Alarms("LL");
        ll.Add("LL_pm", 1e-1f, CondMore, SystemState.Idle, "high", guardValve: "LL_A1", guardValveOpen: true);

        var limits = APS.SystemControl.Core.Core.ChamberLimitsOf(
            new Dictionary<string, Alarms> { ["TM"] = tm, ["LL"] = ll });

        Assert.Equal(1e-3f, limits["TM"].IdleCeilingTorr);
        Assert.Null(limits["TM"].ProcessingCeilingTorr);
        Assert.Equal(1e-1f, limits["LL"].IdleCeilingTorr);
        Assert.Equal("LL_A1 open", limits["LL"].IdleUnless);
    }

    [Fact]
    public void Lowest_idle_ceiling_wins_when_a_chamber_has_several()
    {
        var a = new Alarms("Main");
        a.Add("pm", 1e-2f, CondMore, SystemState.Idle, "loose");
        a.Add("pm", 1e-4f, CondMore, SystemState.Idle, "strict");

        var limits = APS.SystemControl.Core.Core.ChamberLimitsOf(new Dictionary<string, Alarms> { ["Main"] = a });

        Assert.Equal(1e-4f, limits["Main"].IdleCeilingTorr);
    }

    [Fact]
    public void Less_than_rules_are_not_ceilings()
    {
        var a = new Alarms("Main");
        a.Add("pm", 1e-9f, -1, SystemState.Idle, "gauge dead");

        var limits = APS.SystemControl.Core.Core.ChamberLimitsOf(new Dictionary<string, Alarms> { ["Main"] = a });

        Assert.Empty(limits);
    }

    [Fact]
    public void Device_chamber_map_covers_sensors_mfcs_and_controllers()
    {
        var core = NewCore();
        core.hwElements.Item[DeviceType.Sensor].Add("C1_pm",
            new BaseSensor(new BaseDeviceParams("C1_pm", 0, "s")) { Chamber = "C1" });
        core.hwElements.Item[DeviceType.MFC].Add("C1_Ar",
            new BaseMFC(new BaseDeviceParams("C1_Ar", 0, "m")) { Chamber = "C1" });
        core.hwElements.Item[DeviceType.Controller].Add("C1_TC",
            new BaseController(new BaseDeviceParams("C1_TC", 0, "c")) { Chamber = "C1" });
        core.hwElements.Item[DeviceType.Valve].Add("C1_V",
            new BaseValve(new BaseDeviceParams("C1_V", 0, "v")) { Chamber = "C1" });

        var map = APS.SystemControl.Core.Core.DeviceChamberMap(core.hwElements);

        Assert.Equal("C1", map["C1_pm"]);
        Assert.Equal("C1", map["C1_Ar"]);
        Assert.Equal("C1", map["C1_TC"]);
        Assert.DoesNotContain("C1_V", map.Keys);
    }

    private static APS.SystemControl.Core.Core CoreWithMachineFacts()
    {
        var core = NewCore();
        core.hwElements.Item[DeviceType.Stage].Add("rotation", Stage("rotation", true, "false"));
        core.hwElements.Item[DeviceType.Sensor].Add("pm",
            new BaseSensor(new BaseDeviceParams("pm", 0, "s")) { Chamber = "Main" });
        core.hwElements.Item[DeviceType.MFC].Add("Ar1",
            new BaseMFC(new BaseDeviceParams("Ar1", 0, "m")) { Chamber = "Main", GateValves = ["GV1"] });
        core.chamberAlarms["Main"] = AdsMainAlarms();
        return core;
    }

    [Fact]
    public void Factory_semantics_carry_operational_gating_chamber_and_limit_facts()
    {
        var sem = CoreWithMachineFacts().BuildRecipeSemantics();

        Assert.False(sem.RotationOperational);
        Assert.Equal(new[] { "GV1" }, sem.MfcGateValves["Ar1"].ToArray());
        Assert.Equal("Main", sem.ChamberOf("pm"));
        Assert.Equal("Main", sem.ChamberOf("Ar1"));
        Assert.Equal(1e-4f, sem.LimitsOf("Main")!.IdleCeilingTorr);
    }

    [Fact]
    public void Factory_gives_every_construction_site_the_same_facts()
    {
        var core = CoreWithMachineFacts();

        var viaPublic = core.BuildRecipeValidator();
        var viaSemantics = core.BuildRecipeValidator(core.BuildRecipeSemantics());
        var a = core.BuildRecipeSemantics();
        var b = core.BuildRecipeSemantics();

        Assert.NotNull(viaPublic);
        Assert.NotNull(viaSemantics);
        Assert.Equal(a.RotationOperational, b.RotationOperational);
        Assert.Equal(a.MfcGateValves.Keys.Order(), b.MfcGateValves.Keys.Order());
        Assert.Equal(a.LimitsOf("Main"), b.LimitsOf("Main"));
    }

    [Fact]
    public void Disconnected_rotation_stage_is_not_operational_even_without_the_attribute()
    {
        var core = NewCore();
        core.hwElements.Item[DeviceType.Stage].Add("rotation", Stage("rotation", false, null));

        Assert.False(core.BuildRecipeSemantics().RotationOperational);
    }

    [Fact]
    public void Connected_rotation_stage_without_the_attribute_is_operational()
    {
        var core = NewCore();
        core.hwElements.Item[DeviceType.Stage].Add("rotation", Stage("rotation", true, null));

        Assert.True(core.BuildRecipeSemantics().RotationOperational);
    }
}
