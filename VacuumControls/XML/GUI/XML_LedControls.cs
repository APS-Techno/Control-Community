using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("led")]
    public class XML_LedControl : XML_BaseControl
    {
        [XmlAttribute("align")]
        public string? Align { get; set; }

        [XmlAttribute("color")]
        public string? Color { get; set; }

        [XmlAttribute("bgcolor")]
        public string? BgColor { get; set; }
    }

    [XmlRoot("leds")]
    public class XML_LedControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("led")]
        public List<XML_LedControl>? Items { get; set; }
    }
}
