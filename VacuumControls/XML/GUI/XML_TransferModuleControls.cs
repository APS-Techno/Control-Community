using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("transferModule")]
    public class XML_TransferModuleControl : XML_BaseControl
    {
        /// <summary>Chamber ID used to look up the TM pressure sensor and pumps.</summary>
        [XmlAttribute("chamber")]
        public string? Chamber { get; set; }

        /// <summary>
        /// Legacy pixel width. Ignored — the widget self-sizes from the canvas scale
        /// (BaseScale → CalculateSize). Retained so older Controls.xml still deserializes.
        /// </summary>
        [XmlAttribute("width")]
        public int Width { get; set; }

        /// <summary>
        /// Legacy pixel height. Ignored — the widget self-sizes from the canvas scale
        /// (BaseScale → CalculateSize). Retained so older Controls.xml still deserializes.
        /// </summary>
        [XmlAttribute("height")]
        public int Height { get; set; }
    }

    [XmlRoot("transferModules")]
    public class XML_TransferModuleControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("transferModule")]
        public List<XML_TransferModuleControl>? Items { get; set; }
    }
}
