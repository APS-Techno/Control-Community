using System.Xml.Serialization;
using APS.Controls.XML.GUI;

namespace APS.SystemControl.XML.GUI;

[XmlRoot("spump")]
public class XML_SPumpControl : XML_BaseControl
{
    [XmlAttribute("bgcolor")]
    public string? BgColor { get; set; }

    [XmlAttribute("dir")]
    public string? Direction { get; set; }
}

[XmlRoot("spumps")]
public class XML_SPumpControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("spump")]
    public List<XML_SPumpControl>? Items { get; set; }
}
