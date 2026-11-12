using System.Xml.Serialization;

namespace APS.Controls.XML.GUI;

[XmlRoot("pipe")]
public class XML_Pipe
{
    [XmlAttribute("id")] public string? Id { get; set; }
    [XmlAttribute("from")] public string? From { get; set; }
    [XmlAttribute("to")] public string? To { get; set; }
    [XmlAttribute("bends")] public string? Bends { get; set; }
    [XmlAttribute("gas")] public string? Gas { get; set; }
}

[XmlRoot("pipes")]
public class XML_Pipes
{
    [XmlElement("pipe")] public List<XML_Pipe>? Items { get; set; }
}

[XmlRoot("chamber")]
public class XML_Chamber
{
    [XmlAttribute("id")] public string? Id { get; set; }
    [XmlAttribute("label")] public string? Label { get; set; }
    [XmlAttribute("left")] public float Left { get; set; }
    [XmlAttribute("top")] public float Top { get; set; }
    [XmlAttribute("width")] public float Width { get; set; }
    [XmlAttribute("height")] public float Height { get; set; }
    [XmlAttribute("wall")] public float Wall { get; set; } = 1.5f;
    [XmlAttribute("top_anchors")] public int TopAnchors { get; set; }
    [XmlAttribute("bottom_anchors")] public int BottomAnchors { get; set; }
    [XmlAttribute("left_anchors")] public int LeftAnchors { get; set; }
    [XmlAttribute("right_anchors")] public int RightAnchors { get; set; }
    [XmlAttribute("sensor")] public string? Sensor { get; set; }
}

[XmlRoot("chambers")]
public class XML_Chambers
{
    [XmlElement("chamber")] public List<XML_Chamber>? Items { get; set; }
}

[XmlRoot("schematic")]
public class XML_Schematic
{
    [XmlElement("pipes")] public XML_Pipes? Pipes { get; set; }
    [XmlElement("chambers")] public XML_Chambers? Chambers { get; set; }
}
