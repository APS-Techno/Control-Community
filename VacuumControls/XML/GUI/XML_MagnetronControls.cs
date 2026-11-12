using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("magnetron")]
public class XML_MagnetronControl : XML_BaseControl
{
    [XmlAttribute("gas")]
    public string? Gas { get; set; }

    [XmlAttribute("target")]
    public string? TargetLabel { get; set; }

    [XmlAttribute("dir")]
    public string? Direction { get; set; }

    [XmlAttribute("gasinlet")]
    public bool GasInlet { get; set; } = true;
}

[XmlRoot("magnetrons")]
public class XML_MagnetronControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("magnetron")]
    public List<XML_MagnetronControl>? Items { get; set; }
}
