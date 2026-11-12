using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("stage")]
    public class XML_StageControl : XML_BaseControl
    {
        [XmlAttribute("MB")]
        public int MB { get; set; }

        [XmlAttribute("index")]
        public int Index { get; set; }
    }

    [XmlRoot("stages")]
    public class XML_StageControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("stage")]
        public List<XML_StageControl>? Items { get; set; }
    }
}
