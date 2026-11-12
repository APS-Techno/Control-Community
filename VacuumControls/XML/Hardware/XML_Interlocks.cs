using System.Xml.Serialization;

namespace APS.Controls.XML.Hardware;

[XmlRoot("interlocks")]
public class XML_Interlocks
{
    [XmlArray("includes")]
    [XmlArrayItem("file")]
    public List<string>? Includes { get; set; }

    [XmlElement("interlock")]
    public List<XML_Interlock>? InterlockList { get; set; }
}

[XmlRoot("interlock")]
public class XML_Interlock
{
    [XmlAttribute("id")]
    public string? Id { get; set; }

    [XmlElement("l")]
    public List<string>? Conditions { get; set; }
}
