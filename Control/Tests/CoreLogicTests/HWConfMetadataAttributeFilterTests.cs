using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;

namespace CoreLogicTests;

/// <summary>
/// HWConf metadata attributes (<c>chamber=</c>, <c>gate=</c>, <c>vent=</c>, ...) must not reach the
/// positional <c>object[]</c> that <c>XMLHardware.Attributes()</c> builds for
/// <see cref="System.Activator.CreateInstance"/>: an extra argument throws
/// <see cref="MissingMethodException"/> and silently kills the entire section.
/// </summary>
public class HWConfMetadataAttributeFilterTests
{
    private static XElement MakeSensorElement(string id = "pm",
        params (string name, string value)[] extra)
    {
        var el = new XElement("sensor", id);
        el.SetAttributeValue("address", "100");
        el.SetAttributeValue("descr", "TestSensor");
        foreach (var (name, value) in extra)
            el.SetAttributeValue(name, value);
        return el;
    }

    private static BaseSensor MakeSensor(string id = "pm")
    {
        var p = new BaseDeviceParams(id, 0, "test");
        return new BaseSensor(p);
    }

    [Fact]
    public void Attributes_without_metadata_returns_value_plus_positional_attrs()
    {
        var el = MakeSensorElement("pm");

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.Equal("pm",         arr[0]);
        Assert.Equal(100,          arr[1]);
        Assert.Equal("TestSensor", arr[2]);
    }

    [Fact]
    public void Attributes_strips_chamber_attribute_from_array()
    {
        var el = MakeSensorElement("pm", ("chamber", "Loadlock"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.Equal("pm",         arr[0]);
        Assert.Equal(100,          arr[1]);
        Assert.Equal("TestSensor", arr[2]);
        Assert.DoesNotContain("Loadlock", arr.OfType<string>());
    }

    [Fact]
    public void Attributes_strips_all_metadata_attributes()
    {
        var el = MakeSensorElement("pm",
            ("chamber",     "Loadlock"),
            ("gate",        "GV1"),
            ("foreline",    "FV1"),
            ("connects",    "Main"),
            ("property",    "Pressure"),
            ("vent",        "true"),
            ("conductance", "10.5"),
            ("delay",       "2"),
            ("speed",       "2300"),
            ("Kp",          "1.0"),
            ("Ki",          "0.1"),
            ("Kd",          "0.01"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.Equal("pm",         arr[0]);
        Assert.Equal(100,          arr[1]);
        Assert.Equal("TestSensor", arr[2]);
    }

    [Fact]
    public void Attributes_strips_operational_attribute_from_array()
    {
        var el = MakeSensorElement("rotation", ("operational", "false"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.Equal("rotation", arr[0]);
    }

    [Fact]
    public void Attributes_strips_delay_attribute_from_array()
    {
        // "delay" is ValveEmulator metadata (ConfigLoader), not a constructor argument.
        var el = MakeSensorElement("pm", ("delay", "2"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.Equal("pm",         arr[0]);
        Assert.Equal(100,          arr[1]);
        Assert.Equal("TestSensor", arr[2]);
        Assert.DoesNotContain(2,   arr.OfType<int>());
    }

    [Fact]
    public void Attributes_filter_is_case_insensitive()
    {
        var el = new XElement("sensor", "pm");
        el.SetAttributeValue("address", "100");
        el.SetAttributeValue("descr", "X");
        el.SetAttributeValue("Chamber", "Main");
        el.SetAttributeValue("VENT", "true");

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
        Assert.DoesNotContain("Main", arr.OfType<string>());
        Assert.DoesNotContain("true", arr.OfType<string>());
    }

    [Fact]
    public void Attributes_value_is_always_first_element()
    {
        var el = MakeSensorElement("myId", ("chamber", "Main"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal("myId", arr[0]);
    }

    [Fact]
    public void ApplySensorChamber_still_reads_chamber_after_Attributes_filters_it()
    {
        var el = MakeSensorElement("pm", ("chamber", "Loadlock"));

        // Mirrors LoadWHSection: positional array first, then ApplySensorChamber on the same XElement.
        var arr = XMLHardware.Attributes(el);
        Assert.Equal(3, arr.Length);

        var sensor = MakeSensor("pm");

        Core.ApplySensorChamber(sensor, el);

        Assert.Equal("Loadlock", sensor.Chamber);
    }

    [Fact]
    public void ApplySensorChamber_defaults_to_Main_when_chamber_absent_and_array_filtered()
    {
        var el = MakeSensorElement("pm");

        var arr = XMLHardware.Attributes(el);
        Assert.Equal(3, arr.Length);

        var sensor = MakeSensor("pm");
        Core.ApplySensorChamber(sensor, el);

        Assert.Equal(CoreConstants.DefaultChamberId, sensor.Chamber);
    }

    [Fact]
    public void Array_length_is_not_inflated_by_chamber_attribute()
    {
        var el = MakeSensorElement("pm", ("chamber", "Main"));

        var arr = XMLHardware.Attributes(el);

        Assert.Equal(3, arr.Length);
    }

    /// <summary>
    /// A culture-sensitive parse on a comma-decimal machine turns "0.05" into 5 ('.' read as a
    /// group separator), silently corrupting a calibration/offset constructor argument.
    /// </summary>
    [Fact]
    public void Attributes_parses_decimal_with_invariant_culture_under_comma_locale()
    {
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                System.Globalization.CultureInfo.GetCultureInfo("de-DE");

            var el = new XElement("sensor", "pm");
            el.SetAttributeValue("offset", "0.05");

            var arr = XMLHardware.Attributes(el);

            Assert.Equal(2, arr.Length);
            Assert.Equal("pm", arr[0]);
            Assert.IsType<double>(arr[1]);
            Assert.Equal(0.05, (double)arr[1], precision: 10);
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }
}
