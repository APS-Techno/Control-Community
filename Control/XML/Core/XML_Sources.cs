using System.Xml.Serialization;

namespace APS.SystemControl.XML.Core;

[XmlRoot("sources")]
public class XML_Sources
{
    [XmlArray("magnetrons")]
    [XmlArrayItem("magnetron")]
    public List<XML_Magnetron>? Magnetrons { get; set; }

    [XmlArray("powersources")]
    [XmlArrayItem("powersource")]
    public List<XML_PowerSource>? PowerSources { get; set; }

    [XmlArray("formats")]
    [XmlArrayItem("format")]
    public List<XML_Format>? Formats { get; set; }

    [XmlArray("magnetronInstances")]
    [XmlArrayItem("magnetronInstance")]
    public List<XML_MagnetronInstance>? MagnetronInstances { get; set; }

    [XmlElement("layout")]
    public XML_Layout? Layout { get; set; }

    [XmlElement("source")]
    public List<XML_Source>? Sources { get; set; }
}

public class XML_Magnetron
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlText]
    public string DisplayName { get; set; } = "";
}

public class XML_PowerSource
{
    // New form: <powersource id="DC1" elnLink="http://eln/..."/>.
    [XmlAttribute("id")]
    public string? IdAttr { get; set; }

    [XmlAttribute("elnLink")]
    public string? ElnLink { get; set; }

    // Legacy form: <powersource>DC1</powersource>. Kept so existing Sources.xml still loads.
    [XmlText]
    public string? Text { get; set; }

    /// <summary>Effective id: the attribute when present, else the legacy text node.</summary>
    [XmlIgnore]
    public string Id => !string.IsNullOrWhiteSpace(IdAttr) ? IdAttr! : (Text ?? "").Trim();
}

public class XML_Format
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlText]
    public string Label { get; set; } = "";
}

public class XML_MagnetronInstance
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlElement("label")]
    public string Label { get; set; } = "";

    [XmlElement("serial")]
    public string? Serial { get; set; }

    [XmlElement("modelId")]
    public string ModelId { get; set; } = "";

    [XmlElement("format")]
    public string? Format { get; set; }

    [XmlElement("physicsJson")]
    public string? PhysicsJson { get; set; }

    [XmlElement("comment")]
    public string? Comment { get; set; }

    [XmlElement("elnLink")]
    public string? ElnLink { get; set; }

    [XmlElement("state")]
    public string State { get; set; } = "Active";

    [XmlElement("installedDate")]
    public string? InstalledDate { get; set; }
}

public class XML_Source
{
    [XmlAttribute("flange")]
    public int FlangeNumber { get; set; }

    // Cluster systems tag each source with its chamber + per-source shutter.
    // Nullable so XmlSerializer omits the attribute entirely when absent —
    // single-chamber configs (Sputter) round-trip without gaining empty attrs.
    [XmlAttribute("chamber")]
    public string? Chamber { get; set; }

    [XmlAttribute("shutterId")]
    public string? ShutterId { get; set; }

    [XmlAttribute("angle")]
    public string? Angle { get; set; }

    // Working-distance geometry (mm). Stored as InvariantCulture strings so Sources.xml
    // round-trips regardless of machine locale; XmlSerializer omits them when null.
    [XmlAttribute("zeroDistanceMm")]
    public string? ZeroDistanceMm { get; set; }

    [XmlAttribute("liftMm")]
    public string? LiftMm { get; set; }

    // Operator-facing source handle. Nullable so XmlSerializer omits it when
    // unset — single-source configs round-trip without gaining an empty attr.
    [XmlAttribute("name")]
    public string? Name { get; set; }

    [XmlElement("material")]
    public string? Material { get; set; }

    [XmlElement("magnetronId")]
    public string? MagnetronId { get; set; }

    [XmlElement("magnetronInstanceId")]
    public string? MagnetronInstanceId { get; set; }

    [XmlElement("powerSupplyId")]
    public string? PowerSupplyId { get; set; }

    [XmlElement("model")]
    public string? Model { get; set; }

    [XmlElement("powerType")]
    public string? PowerType { get; set; }

    [XmlElement("description")]
    public string? Description { get; set; }

    [XmlElement("comment")]
    public string? Comment { get; set; }

    [XmlElement("installedDate")]
    public string? InstalledDate { get; set; }

    [XmlElement("totalDepositionTime")]
    public string? TotalDepositionTime { get; set; }
}

public class XML_Layout
{
    [XmlAttribute("start")]
    public string? Start { get; set; }

    [XmlAttribute("direction")]
    public string? Direction { get; set; }   // "cw" | "ccw"

    [XmlElement("chamber")]
    public List<XML_LayoutChamber>? Chambers { get; set; }
}

public class XML_LayoutChamber
{
    [XmlAttribute("id")]
    public string Id { get; set; } = "";

    [XmlAttribute("start")]
    public string? Start { get; set; }

    [XmlAttribute("direction")]
    public string? Direction { get; set; }
}
