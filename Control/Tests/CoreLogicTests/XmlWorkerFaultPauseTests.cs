using System.Xml.Serialization;
using APS.SystemControl.XML.Core;
using Xunit;

namespace CoreLogicTests;

public class XmlWorkerFaultPauseTests
{
    private static XML_Worker Load(string xml)
    {
        var ser = new XmlSerializer(typeof(XML_Worker));
        using var reader = new StringReader(xml);
        return (XML_Worker)ser.Deserialize(reader)!;
    }

    [Fact]
    public void Absent_attribute_defaults_to_600()
        => Assert.Equal(600, Load("<worker><library lib=\"X\">X.dll</library></worker>").FaultPauseTimeoutSeconds);

    [Fact]
    public void Attribute_is_read()
        => Assert.Equal(120, Load("<worker faultPauseTimeoutSeconds=\"120\"><library lib=\"X\">X.dll</library></worker>").FaultPauseTimeoutSeconds);

    [Fact]
    public void Zero_is_preserved_so_pausing_can_be_disabled()
        => Assert.Equal(0, Load("<worker faultPauseTimeoutSeconds=\"0\"><library lib=\"X\">X.dll</library></worker>").FaultPauseTimeoutSeconds);

    // heldWarningSeconds is the only thing that surfaces an unattended hold.

    [Fact]
    public void Absent_held_warning_defaults_to_900()
        => Assert.Equal(900, Load("<worker><library lib=\"X\">X.dll</library></worker>").HeldWarningSeconds);

    [Fact]
    public void Held_warning_attribute_is_read()
        => Assert.Equal(120, Load("<worker heldWarningSeconds=\"120\"><library lib=\"X\">X.dll</library></worker>").HeldWarningSeconds);

    [Fact]
    public void Zero_is_preserved_so_the_hold_warning_can_be_disabled()
        => Assert.Equal(0, Load("<worker heldWarningSeconds=\"0\"><library lib=\"X\">X.dll</library></worker>").HeldWarningSeconds);
}
