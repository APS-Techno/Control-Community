using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("MFC")]
    public class XML_MFCControl : XML_BaseControl
    {
        [XmlAttribute("gas")]
        public string? Gas { get; set; }
    }

    [XmlRoot("MFCs")]
    public class XML_MFCControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("MFC")]
        public List<XML_MFCControl>? Items { get; set; }
    }
}
