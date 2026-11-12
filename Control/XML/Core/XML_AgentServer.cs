using System.Xml.Serialization;

namespace APS.SystemControl.XML.Core;

[XmlRoot("AgentServer")]
public class XML_AgentServer
{
    [XmlAttribute("enabled")] public bool Enabled { get; set; }
    [XmlAttribute("port")] public int Port { get; set; } = 8026;
    [XmlAttribute("bind")] public string Bind { get; set; } = "127.0.0.1";
    [XmlAttribute("allowRecipe")] public bool AllowRecipe { get; set; } = true;
    [XmlAttribute("allowManual")] public bool AllowManual { get; set; }
    [XmlElement("Key")] public List<XML_AgentKey> Keys { get; set; } = [];
    [XmlElement("Bus")] public XML_AgentBus? Bus { get; set; }
    [XmlElement("ControlMode")] public XML_AgentControlMode? ControlMode { get; set; }
}

public class XML_AgentKey
{
    [XmlAttribute("name")] public string Name { get; set; } = "";
    [XmlAttribute("hash")] public string Hash { get; set; } = "";
    [XmlAttribute("tier")] public string Tier { get; set; } = "Read";
    /// <summary>Optional: one folder under the recipes directory that this key's uploads go to and
    /// that its recipe listing is confined to. Blank/missing = the whole library.</summary>
    [XmlAttribute("recipeFolder")] public string? RecipeFolder { get; set; }
}

public class XML_AgentBus
{
    [XmlAttribute("samplerMs")] public int SamplerMs { get; set; } = 250;
    [XmlAttribute("queueDepth")] public int QueueDepth { get; set; } = 1000;
    /// <summary>0 = off: change-only. > 0 re-reports every point that often so a quiet machine
    /// still produces samples for a time-series store.</summary>
    [XmlAttribute("heartbeatMs")] public int HeartbeatMs { get; set; }
}

public class XML_AgentControlMode
{
    /// <summary>Restore Online-Remote across a Control restart when the last state was Remote.</summary>
    [XmlAttribute("restoreRemote")] public bool RestoreRemote { get; set; }
}
