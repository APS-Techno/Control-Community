using System.Xml.Serialization;

namespace APS.Controls.XML.GUI
{
    public class XML_BaseControl
    {
        private float _scale = 1f;

        [XmlAttribute("id")]
        public string? Id { get; set; }

        [XmlAttribute("left")]
        public float Left { get; set; }

        [XmlAttribute("top")]
        public float Top { get; set; }

        [XmlText]
        public string? Description { get; set; }

        [XmlAttribute("scale")]
        public float Scale
        {
            get => _scale;
            set
            {
                _scale = value;
                ScaleSet = true;
            }
        }
        [XmlIgnore]
        public bool ScaleSet { get; set; }
    }
}
