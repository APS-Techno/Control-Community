using System.Xml.Linq;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.XML;

namespace CoreLogicTests;

/// <summary>
/// A hardware element's text Value is its id, used verbatim as the constructor
/// id (<see cref="XMLHardware.Attributes"/>) and as the registration key
/// (<c>Elements.Add</c>). The &lt;robot&gt; element is the only one with child
/// elements (&lt;face&gt;/&lt;timing&gt;), so its Value is the id PLUS the
/// inter-element whitespace. Both call sites must trim, or the device registers
/// under a padded key and recipe device-binding (FindElement) can't locate
/// "ROBOT" — which aborts SYSTEM_START at its trailing `home` step.
/// </summary>
public class RobotDeviceIdTrimTests
{
    // Mirrors Configs/Cluster_Demo/HWConf.xml: id text on its own line, followed
    // by <face>/<timing> children — so element.Value is padded with whitespace.
    private static XElement MakeRobotElement()
    {
        var robot = new XElement("robot",
            "\n      ROBOT\n      ",
            new XElement("face",
                new XAttribute("name", "LL"), new XAttribute("idx", "1"),
                new XAttribute("slit", "SV_LL"), new XAttribute("angle", "0")),
            new XElement("timing", new XAttribute("rotateMs", "2000")));
        robot.SetAttributeValue("address", "220");
        robot.SetAttributeValue("cmdIndex", "0");
        robot.SetAttributeValue("statusIndex", "4");
        robot.SetAttributeValue("descr", "Transfer robot");
        return robot;
    }

    [Fact]
    public void Attributes_trims_robot_id_despite_child_elements()
    {
        var arr = XMLHardware.Attributes(MakeRobotElement());
        // Attr[0] is the constructor id.
        Assert.Equal("ROBOT", arr[0]);
    }

    [Fact]
    public void Elements_Add_trims_key_so_lookup_by_id_succeeds()
    {
        var coll = new Elements();
        IBaseComponent robot = new BaseSensor(new BaseDeviceParams("ROBOT", 0, "Transfer robot"));
        coll.Add("\n      ROBOT\n      ", robot);   // padded key, as LoadWHSection would pass
        Assert.Same(robot, coll["ROBOT"]);
    }
}
