using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("pump")]
public class XML_PumpControl : XML_BaseControl
{
    [XmlAttribute("bgcolor")]
    public string? BgColor { get; set; }
}

[XmlRoot("pumps")]
public class XML_PumpControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("pump")]
    public List<XML_PumpControl>? Items { get; set; }
}


[XmlRoot("tpump")]
public class XML_TPumpControl : XML_BaseControl
{
    [XmlAttribute("bgcolor")]
    public string? BgColor { get; set; }

    [XmlAttribute("dir")]
    public string? Direction { get; set; }
}

[XmlRoot("tpumps")]
public class XML_TPumpControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("tpump")]
    public List<XML_TPumpControl>? Items { get; set; }
}

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
