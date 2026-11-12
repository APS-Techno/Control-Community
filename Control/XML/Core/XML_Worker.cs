using System.Xml.Serialization;

namespace APS.SystemControl.XML.Core;

[XmlRoot("worker")]
public class XML_Worker
{
    /// <summary>Fault-pause watchdog, seconds. Absent → 600. 0 → pausing disabled.
    /// See docs/superpowers/specs/2026-09-04-fault-pause-design.md.</summary>
    [XmlAttribute("faultPauseTimeoutSeconds")]
    public int FaultPauseTimeoutSeconds { get; set; } = 600;

    /// <summary>How long a HELD fault pause runs before it starts warning, seconds. Absent →
    /// 900. 0 → no warning at all. A hold never auto-aborts (its watchdog is disarmed on
    /// purpose), so this is the only thing that makes an unattended one visible.</summary>
    [XmlAttribute("heldWarningSeconds")]
    public int HeldWarningSeconds { get; set; } = 900;

    [XmlElement("library")]
    public XML_Library? Library { get; set; }

    [XmlElement("operations")]
    public XML_Operations? Operations { get; set; }

    [XmlArray("vars")]
    [XmlArrayItem("var")]
    public List<XML_Variable>? Vars { get; set; }
}

public class XML_Library
{
    [XmlAttribute("lib")]
    public string? Lib { get; set; }

    /// <summary>
    /// Optional SHA-256 hash (lower-case hex) of the DLL file. When set, the
    /// loader refuses to load the DLL if the computed hash differs.
    /// </summary>
    [XmlAttribute("sha256")]
    public string? Sha256 { get; set; }

    [XmlText]
    public string? Path { get; set; }
}

public class XML_Operations
{
    [XmlAttribute("typename")]
    public string? Typename { get; set; }

    [XmlElement("op")]
    public List<XML_Operation>? Ops { get; set; }
}

public class XML_Operation
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlAttribute("method")]
    public string? Method { get; set; }

    /// <summary>
    /// Optional bound device name. When set, the dispatch auto-injects
    /// this device as the first arg instead of reading it from the recipe line.
    /// </summary>
    [XmlAttribute("device")]
    public string? Device { get; set; }

    [XmlText]
    public string? Name { get; set; }

    [XmlElement("var")]
    public List<string>? Vars { get; set; }
}

public class XML_Variable
{
    [XmlAttribute("type")]
    public string? Type { get; set; }

    [XmlText]
    public string? Name { get; set; }
}
