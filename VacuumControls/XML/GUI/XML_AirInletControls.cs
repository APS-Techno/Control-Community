using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("airinlet")]
public class XML_AirInletControl : XML_BaseControl
{
    [XmlAttribute("dir")]
    public string? Direction { get; set; }
}

[XmlRoot("airinlets")]
public class XML_AirInletControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("airinlet")]
    public List<XML_AirInletControl>? Items { get; set; }
}
