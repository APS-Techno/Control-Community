using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("sensor")]
public class XML_Sensor : XML_BaseControl
{
    [XmlAttribute("unit")]
    public string? Unit { get; set; }

    [XmlAttribute("format")]
    public string? Format { get; set; }

    [XmlAttribute("color")]
    public string? Color { get; set; }

    [XmlAttribute("direction")]
    public string? Direction { get; set; }
}

[XmlRoot("sensors")]
public class XML_Sensors
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("sensor")]
    public List<XML_Sensor>? Items { get; set; }
}
