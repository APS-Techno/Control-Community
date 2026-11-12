using APS.ProcessPanel.Contracts;
using System.Text.Json;
using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.XML;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Remote.Agent;

namespace CoreLogicTests;

public class AgentDescribeHardwareTests
{
    private static JsonElement Project(string name, IBaseComponent dev) =>
        JsonDocument.Parse(JsonSerializer.Serialize(
            CoreAgentSystemAccess.DescribeDevice(name, dev), AgentProtocol.Opt)).RootElement;

    private static BaseMFC Mfc(string id) => new(new BaseDeviceParams(id, 0, "mfc"));

    private static XElement MfcElement(string id, string? valve)
    {
        var el = new XElement("mfc", id);
        el.SetAttributeValue("address", "168");
        el.SetAttributeValue("descr", "MFC");
        if (valve is not null) el.SetAttributeValue("valve", valve);
        return el;
    }

    [Fact]
    public void Gated_mfc_lists_its_valves_with_or_logic()
    {
        var mfc = Mfc("Ar1");
        Core.ApplyMfcGateValves(mfc, MfcElement("Ar1", "V2, V3"));

        var e = Project("Ar1", mfc);

        Assert.Equal(["V2", "V3"], e.GetProperty("gateValves").EnumerateArray().Select(v => v.GetString()!).ToArray());
        Assert.Equal("OR", e.GetProperty("gatingLogic").GetString());
        Assert.Equal("Ar1", e.GetProperty("id").GetString());
    }

    [Fact]
    public void Ungated_mfc_reports_empty_list_and_none()
    {
        var mfc = Mfc("N21");
        Core.ApplyMfcGateValves(mfc, MfcElement("N21", null));

        var e = Project("N21", mfc);

        Assert.Equal(0, e.GetProperty("gateValves").GetArrayLength());
        Assert.Equal("none", e.GetProperty("gatingLogic").GetString());
    }

    [Fact]
    public void Blank_entries_in_a_valve_list_are_dropped()
    {
        var mfc = Mfc("Ar1");
        Core.ApplyMfcGateValves(mfc, MfcElement("Ar1", " V1 ,, V2 "));

        Assert.Equal(["V1", "V2"], mfc.GateValves);
    }

    [Fact]
    public void Valve_attribute_is_not_a_constructor_argument()
    {
        var arr = XMLHardware.Attributes(MfcElement("Ar1", "V2,V3"));

        Assert.Equal(3, arr.Length);
        Assert.DoesNotContain("V2,V3", arr.OfType<string>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stage_reports_operational_state_from_its_connection(bool connected)
    {
        var stage = new BaseStage(new BaseDeviceParams("rotation", 0, "stage")) { Connected = connected };

        var e = Project("rotation", stage);

        Assert.Equal(connected, e.GetProperty("isOperational").GetBoolean());
        Assert.Equal(connected, e.GetProperty("connected").GetBoolean());
    }

    [Fact]
    public void Other_devices_keep_their_original_shape()
    {
        var valve = new BaseValve(new BaseDeviceParams("V1", 0, "valve"));

        var e = Project("V1", valve);

        Assert.False(e.TryGetProperty("gateValves", out _));
        Assert.False(e.TryGetProperty("isOperational", out _));
        Assert.Equal("Main", e.GetProperty("chamber").GetString());
    }

    [Fact]
    public void Source_to_supply_map_keys_on_the_effective_name()
    {
        var map = CoreAgentSystemAccess.SourceToPsuMap(
        [
            new FlangeSourceInfo("C", "PSU-B", null),
            new FlangeSourceInfo("Ru", "RF601", null, Name: "Ru_front"),
            new FlangeSourceInfo("Si", null, null),
        ]);

        Assert.Equal("PSU-B", map["c"]);
        Assert.Equal("RF601", map["Ru_front"]);
        Assert.DoesNotContain("Ru", map.Keys);
        Assert.DoesNotContain("Si", map.Keys);
    }

    [Fact]
    public void Source_name_shared_by_different_supplies_is_dropped()
    {
        var map = CoreAgentSystemAccess.SourceToPsuMap(
        [
            new FlangeSourceInfo("Cu", "RF_A", null),
            new FlangeSourceInfo("Cu", "RF_B", null),
            new FlangeSourceInfo("Ti", "RF_A", null),
            new FlangeSourceInfo("Ti", "RF_A", null),
        ]);

        Assert.DoesNotContain("Cu", map.Keys);
        Assert.Equal("RF_A", map["Ti"]);
    }

    [Fact]
    public void Gate_valve_map_lists_only_gated_mfcs()
    {
        var gated = Mfc("Ar1");
        gated.GateValves = ["V2", "V3"];
        var map = CoreAgentSystemAccess.MfcGateValvesMap(
            [("Ar1", gated), ("N21", Mfc("N21")), ("V1", new BaseValve(new BaseDeviceParams("V1", 0, "v")))]);

        Assert.Equal(["V2", "V3"], map["ar1"].ToArray());
        Assert.Single(map);
    }
}
