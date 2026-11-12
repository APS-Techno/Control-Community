using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class SensorChamberLoadingTests
{
    private static BaseSensor MakeSensor(string id = "pm")
    {
        var p = new BaseDeviceParams(id, 0, "test");
        return new BaseSensor(p);
    }

    private static XElement MakeSensorElement(string id, string? chamber)
    {
        var el = new XElement("sensor", id);
        el.SetAttributeValue("address", "0");
        el.SetAttributeValue("descr", "test");
        if (chamber is not null)
            el.SetAttributeValue("chamber", chamber);
        return el;
    }

    [Fact]
    public void Chamber_defaults_to_Main_when_attribute_absent()
    {
        var sensor = MakeSensor();
        Assert.Equal(CoreConstants.DefaultChamberId, sensor.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_sets_chamber_from_element_attribute()
    {
        var sensor  = MakeSensor();
        var element = MakeSensorElement("pm", "Loadlock");

        Core.ApplySensorChamber(sensor, element);

        Assert.Equal("Loadlock", sensor.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_falls_back_to_default_when_attribute_absent()
    {
        var sensor  = MakeSensor();
        var element = MakeSensorElement("pm", null);

        Core.ApplySensorChamber(sensor, element);

        Assert.Equal(CoreConstants.DefaultChamberId, sensor.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_overwrites_existing_chamber_with_default_when_attribute_absent()
    {
        var sensor  = MakeSensor();
        sensor.Chamber = "Custom";
        var element = MakeSensorElement("pm", null);

        Core.ApplySensorChamber(sensor, element);

        Assert.Equal(CoreConstants.DefaultChamberId, sensor.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_sets_chamber_on_a_valve()
    {
        var valve   = new BaseValve(new BaseDeviceParams("C1_V", 0, "test"));
        var element = MakeSensorElement("C1_V", "C1");

        Core.ApplySensorChamber(valve, element);

        Assert.Equal("C1", valve.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_sets_chamber_on_an_mfc()
    {
        var mfc     = new BaseMFC(new BaseDeviceParams("C1_Ar", 0, "test"));
        var element = MakeSensorElement("C1_Ar", "C1");

        Core.ApplySensorChamber(mfc, element);

        Assert.Equal("C1", mfc.Chamber);
    }

    [Fact]
    public void Valve_and_mfc_chamber_default_to_Main_when_attribute_absent()
    {
        var valve = new BaseValve(new BaseDeviceParams("V", 0, "test"));
        var mfc   = new BaseMFC(new BaseDeviceParams("M", 0, "test"));

        Core.ApplySensorChamber(valve, MakeSensorElement("V", null));
        Core.ApplySensorChamber(mfc,   MakeSensorElement("M", null));

        Assert.Equal(CoreConstants.DefaultChamberId, valve.Chamber);
        Assert.Equal(CoreConstants.DefaultChamberId, mfc.Chamber);
    }
}
