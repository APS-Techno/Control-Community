using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("gasinlet")]
public class XML_GasInletControl : XML_BaseControl
{
    [XmlAttribute("gas")]
    public string? Gas { get; set; }

    [XmlAttribute("dir")]
    public string? Direction { get; set; }
}

[XmlRoot("gasinlets")]
public class XML_GasInletControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("gasinlet")]
    public List<XML_GasInletControl>? Items { get; set; }
}
