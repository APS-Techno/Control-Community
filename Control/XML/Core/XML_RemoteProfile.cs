using System.Xml.Serialization;

namespace APS.SystemControl.XML.Core;

[XmlRoot("RemoteProfile")]
public class XML_RemoteProfile
{
    [XmlAttribute("pipe")] public string Pipe { get; set; } = "APS.Control.Remote";
    [XmlElement("Slot")] public List<XML_RemoteSlot> Slots { get; set; } = [];
}

public class XML_RemoteSlot
{
    [XmlAttribute("index")] public int Index { get; set; }
    [XmlAttribute("label")] public string Label { get; set; } = "";
    [XmlAttribute("icon")] public string Icon { get; set; } = "";
    [XmlAttribute("type")] public string Type { get; set; } = "";
    [XmlAttribute("device")] public string Device { get; set; } = "";
    [XmlAttribute("op")] public string Op { get; set; } = "";
    [XmlAttribute("arg")] public string Arg { get; set; } = "";
    [XmlAttribute("status")] public string Status { get; set; } = "";
    [XmlAttribute("confirm")] public bool Confirm { get; set; }
    [XmlAttribute("permission")] public string Permission { get; set; } = "";
}
