using System.ComponentModel;
using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("valve")]
    public class XML_ValveControl : XML_BaseControl
    {
        [XmlAttribute("dir")]
        [DefaultValue("hor")]
        public string? Direction { get; set; } = "hor";
    }

    [XmlRoot("valves")]
    public class XML_ValveControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("valve")]
        public List<XML_ValveControl>? Items { get; set; }
    }

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

    [XmlRoot("throttlevalve")]
    public class XML_ThrottleValveControl : XML_BaseControl
    {
        [XmlAttribute("dir")]
        [DefaultValue("hor")]
        public string? Direction { get; set; } = "hor";
    }

    [XmlRoot("throttlevalves")]
    public class XML_ThrottleValveControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("throttlevalve")]
        public List<XML_ThrottleValveControl>? Items { get; set; }
    }
}
