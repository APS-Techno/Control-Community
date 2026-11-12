using System.Xml.Serialization;

namespace APS.SystemControl.XML;

[XmlRoot("alarms")]
public class XML_Alarms
{
    [XmlElement("chamber")]
    public List<XML_Chamber> Chambers { get; set; } = [];

    // Flat (no chamber wrapper) — interpreted as the implicit Main chamber.
    [XmlElement("states")]
    public XML_States? States { get; set; }

    [XmlElement("alarm")]
    public List<XML_Alarm> AlarmList { get; set; } = [];
}

public class XML_Chamber
{
    [XmlAttribute("id")]
    public string? Id { get; set; }

    [XmlElement("states")]
    public XML_States? States { get; set; }

    [XmlElement("alarm")]
    public List<XML_Alarm> AlarmList { get; set; } = [];
}

public class XML_States
{
    [XmlAttribute("initial")]
    public string? Initial { get; set; }

    [XmlElement("transition")]
    public List<XML_Transition> Transitions { get; set; } = [];
}

public class XML_Transition
{
    [XmlAttribute("from")]
    public string? From { get; set; }

    [XmlAttribute("to")]
    public string? To { get; set; }

    // Sensor-based transition attributes
    [XmlAttribute("sensor")]
    public string? Sensor { get; set; }

    [XmlAttribute("cond")]
    public string? Condition { get; set; }

    [XmlAttribute("val")]
    public string? Value { get; set; }

    // Valve-action transition attributes
    [XmlAttribute("valve")]
    public string? Valve { get; set; }

    [XmlAttribute("state")]
    public string? State { get; set; }

    [XmlAttribute("require")]
    public string? Require { get; set; }

    // Group-based transition attributes
    [XmlAttribute("group")]
    public string? Group { get; set; }
}

public class XML_Alarm
{
    // Sensor-based alarm attributes (mutually exclusive with device-based)
    [XmlAttribute("sensor")]
    public string? Sensor { get; set; }

    [XmlAttribute("cond")]
    public string? Condition { get; set; }

    [XmlAttribute("val")]
    public string? Value { get; set; }

    // Device-status alarm attributes (mutually exclusive with sensor-based)
    [XmlAttribute("device")]
    public string? Device { get; set; }

    [XmlAttribute("status")]
    public string? Status { get; set; }

    [XmlAttribute("state")]
    public string? State { get; set; }

    // Optional guard: suppress this alarm while the named valve is in the given
    // state, e.g. unless="LL_A1 open" — a controlled vent is not a fault.
    [XmlAttribute("unless")]
    public string? Unless { get; set; }

    // Optional settling delay in seconds, e.g. delay="60". The condition must
    // hold CONTINUOUSLY for this long before the alarm is raised; if it clears
    // first, the timer restarts. Omitted (or 0) keeps the original behaviour of
    // firing on the first matching sample. Use for a state whose entry
    // condition races the physics — e.g. a chamber enters `idle` the instant
    // the last gas valve closes, while still at process pressure.
    [XmlAttribute("delay")]
    public string? Delay { get; set; }

    [XmlText]
    public string? Message { get; set; }
}
