using System.Xml.Serialization;

namespace APS.SystemControl.XML.Core;

[XmlRoot("recipes")]
public class XML_Recipes
{
    [XmlElement("recipe")]
    public List<XML_Recipe>? RecipesList { get; set; }
}

public class XML_Recipe
{
    [XmlAttribute("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Step-failure policy: "continue" (attempt every step, collect failures,
    /// report INCOMPLETE) or "abort" (stop at the first failing step). Absent
    /// means "abort" — a recipe is strict unless it says otherwise.
    /// </summary>
    [XmlAttribute("onStepFailure")]
    public string? OnStepFailure { get; set; }

    /// <summary>
    /// Text the operator confirms before a panel button starts this recipe — what
    /// it will do to the machine. Absent means a generic "Start &lt;id&gt;?" prompt.
    /// </summary>
    [XmlAttribute("confirm")]
    public string? Confirm { get; set; }

    /// <summary>
    /// Chamber states (comma/space separated: "processing", "processing maintenance")
    /// in which a panel button or remote host may NOT start this recipe. Absent means
    /// no restriction. Nested 'call' from a running recipe is never restricted.
    /// </summary>
    [XmlAttribute("notIn")]
    public string? NotIn { get; set; }

    [XmlElement("l")]
    public List<string>? Lines { get; set; }
}
