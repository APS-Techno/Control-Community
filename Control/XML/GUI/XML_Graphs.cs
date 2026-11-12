using System;
using System.Collections.Generic;
using System.Xml.Serialization;

[XmlRoot("Graphs")]
public class XML_Graphs
{
    [XmlAttribute("panel")]
    public string? Panel { get; set; }

    [XmlElement("Graph")]
    public List<XML_Graph> Graphs { get; set; } = new();
}

public class XML_Graph
{
    [XmlText]
    public string? Name { get; set; }

    [XmlElement("Units")]
    public string? Units { get; set; }

    [XmlElement("Format")]
    public string? Format { get; set; }

    [XmlElement("Min")]
    public double Min { get; set; }

    [XmlElement("Max")]
    public double Max { get; set; }

    [XmlElement("YAxis")]
    public string? YAxis { get; set; }

    [XmlElement("Color")]
    public string? Color { get; set; }

    [XmlElement("Sensor")]
    public string? Sensor { get; set; }
}
