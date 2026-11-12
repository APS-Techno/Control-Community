using System.ComponentModel;
using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    [XmlRoot("tconnector")]
    public class XML_TConnectorControl : XML_BaseControl
    {
        [XmlAttribute("dir")]
        [DefaultValue("down")]
        public string? Direction { get; set; } = "down";
    }

    [XmlRoot("tconnectors")]
    public class XML_TConnectorControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("tconnector")]
        public List<XML_TConnectorControl>? Items { get; set; }
    }

    [XmlRoot("crossconnector")]
    public class XML_CrossConnectorControl : XML_BaseControl
    {
    }

    [XmlRoot("crossconnectors")]
    public class XML_CrossConnectorControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("crossconnector")]
        public List<XML_CrossConnectorControl>? Items { get; set; }
    }

    [XmlRoot("gasmixer")]
    public class XML_GasMixerControl : XML_BaseControl
    {
        [XmlAttribute("dir")]
        [DefaultValue("hor")]
        public string? Direction { get; set; } = "hor";

        [XmlAttribute("inputs")]
        [DefaultValue(2)]
        public int Inputs { get; set; } = 2;

        [XmlAttribute("outputs")]
        [DefaultValue(1)]
        public int Outputs { get; set; } = 1;
    }

    [XmlRoot("gasmixers")]
    public class XML_GasMixerControls
    {
        [XmlAttribute("type")]
        public string? Type { get; set; }

        [XmlElement("gasmixer")]
        public List<XML_GasMixerControl>? Items { get; set; }
    }
}
