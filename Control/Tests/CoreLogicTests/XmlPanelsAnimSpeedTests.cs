using System.Xml.Serialization;
using APS.SystemControl.XML.GUI;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Panels.xml &lt;animFlangeSeconds&gt; round-trip: the per-system drive-icon
/// sweep speed must survive PanelDragManager's re-save, and panels that never
/// set it must not gain a spurious &lt;animFlangeSeconds&gt;0&lt;/animFlangeSeconds&gt;
/// element (ShouldSerialize guard).
/// </summary>
public class XmlPanelsAnimSpeedTests
{
    private static string Serialize(XML_Panels panels)
    {
        var ser = new XmlSerializer(typeof(XML_Panels));
        using var sw = new StringWriter();
        ser.Serialize(sw, panels);
        return sw.ToString();
    }

    private static XML_Panels Deserialize(string xml)
    {
        var ser = new XmlSerializer(typeof(XML_Panels));
        using var sr = new StringReader(xml);
        return (XML_Panels)ser.Deserialize(sr)!;
    }

    [Fact]
    public void Anim_speed_elements_round_trip()
    {
        var panels = new XML_Panels
        {
            Panels =
            [
                new XML_Panel
                {
                    ID = "StepperDrives",
                    AnimFlangeSeconds = 0.8f,
                    AnimHolderSeconds = 1.0f,
                    AnimShutterSeconds = 0.5f,
                }
            ]
        };

        var back = Deserialize(Serialize(panels));

        Assert.Equal(0.8f, back.Panels![0].AnimFlangeSeconds);
        Assert.Equal(1.0f, back.Panels![0].AnimHolderSeconds);
        Assert.Equal(0.5f, back.Panels![0].AnimShutterSeconds);
    }

    [Fact]
    public void Unset_anim_speed_elements_are_not_written()
    {
        var panels = new XML_Panels { Panels = [new XML_Panel { ID = "GAUGE_A#1" }] };

        string xml = Serialize(panels);

        Assert.DoesNotContain("animFlangeSeconds", xml);
        Assert.DoesNotContain("animHolderSeconds", xml);
        Assert.DoesNotContain("animShutterSeconds", xml);
    }

    [Fact]
    public void Elements_are_parsed_from_config_markup()
    {
        const string xml = """
            <panels>
              <panel>
                <id>Process</id>
                <animFlangeSeconds>1.5</animFlangeSeconds>
                <animHolderSeconds>2</animHolderSeconds>
                <animShutterSeconds>0.75</animShutterSeconds>
              </panel>
            </panels>
            """;

        var panels = Deserialize(xml);

        Assert.Equal(1.5f, panels.Panels![0].AnimFlangeSeconds);
        Assert.Equal(2f, panels.Panels![0].AnimHolderSeconds);
        Assert.Equal(0.75f, panels.Panels![0].AnimShutterSeconds);
    }
}
