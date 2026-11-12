using System;
using System.Xml.Serialization;

[XmlRoot("Configurations")]
public class XML_Config
{
    [XmlAttribute("name")]
    public string? Name { get; set; }

    [XmlElement("Hardware")]
    public string? Hardware { get; set; }

    [XmlElement("Interlocks")]
    public string? Interlocks { get; set; }

    [XmlElement("Alarms")]
    public string? Alarms { get; set; }

    [XmlElement("Worker")]
    public string? Worker { get; set; }

    [XmlElement("Embeded")]
    public string? Embeded { get; set; }

    [XmlElement("GUI")]
    public string? GUI { get; set; }

    [XmlElement("Panels")]
    public string? Panels { get; set; }

    [XmlElement("Graphs")]
    public string? Graphs { get; set; }
}
