using System.Xml.Serialization;

namespace APS.Controls.XML.Hardware;

[XmlRoot("alarms")]
public class XML_Alarms
{
    [XmlElement("alarm")]
    public List<XML_Alarm>? AlarmList { get; set; }
}

[XmlRoot("alarm")]
public class XML_Alarm
{
    [XmlAttribute("sensor")]
    public string? Sensor { get; set; }

    [XmlAttribute("cond")]
    public string? Condition { get; set; }

    [XmlAttribute("val")]
    public string? Value { get; set; }

    [XmlAttribute("state")]
    public string? State { get; set; }

    [XmlText]
    public string? Message { get; set; }
}
