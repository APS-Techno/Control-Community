using System.ComponentModel;
using System.Xml.Serialization;
using APS.Controls.XML.GUI;

namespace APS.SystemControl.XML.GUI;

[XmlRoot("gatevalve")]
public class XML_GateValveControl : XML_BaseControl
{
    [XmlAttribute("dir")]
    [DefaultValue("hor")]
    public string? Direction { get; set; } = "hor";
}

[XmlRoot("gatevalves")]
public class XML_GateValveControls
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlElement("gatevalve")]
    public List<XML_GateValveControl>? Items { get; set; }
}
