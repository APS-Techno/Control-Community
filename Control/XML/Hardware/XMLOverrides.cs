using System.Xml.Serialization;

namespace APS.SystemControl.XML;

[XmlRoot("overrides")]
public class XML_Overrides
{
    [XmlElement("alarm")]
    public List<XML_Override> Alarms { get; set; } = new();

    [XmlElement("interlock")]
    public List<XML_Override> Interlocks { get; set; } = new();
}

public class XML_Override
{
    [XmlAttribute("id")]
    public string? Id { get; set; }

    [XmlAttribute("disabled")]
    public bool Disabled { get; set; }

    [XmlAttribute("reason")]
    public string? Reason { get; set; }

    [XmlAttribute("disabledAt")]
    public string? DisabledAt { get; set; }

    [XmlAttribute("disabledBy")]
    public string? DisabledBy { get; set; }
}
