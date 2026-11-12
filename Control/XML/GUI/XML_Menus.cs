using System.Xml.Serialization;

namespace APS.SystemControl.XML.GUI;

/// <summary>
/// Root element of Menus.xml. Each &lt;contribution&gt; element produces one
/// MenuItemSpec at composition time. For complex behavior (custom dialogs,
/// dynamic captions) write a [MenuContributor] class instead.
/// </summary>
[XmlRoot(ElementName = "menus")]
public class XML_Menus
{
    [XmlElement(ElementName = "contribution")]
    public List<XML_MenuContribution>? Contributions { get; set; }
}

public class XML_MenuContribution
{
    /// <summary>Slash-separated menu path, e.g. "Tools/Pulse Calculator…".</summary>
    [XmlAttribute("path")]
    public string Path { get; set; } = "";

    /// <summary>Optional sort order within parent.</summary>
    [XmlAttribute("order")]
    public int Order { get; set; }

    /// <summary>Optional icon name, resolved through APS.Icons.IconLibrary.</summary>
    [XmlAttribute("icon")]
    public string? Icon { get; set; }

    /// <summary>Optional shortcut, e.g. "Ctrl+Shift+P".</summary>
    [XmlAttribute("shortcut")]
    public string? Shortcut { get; set; }

    /// <summary>
    /// Action specifier — currently supports two forms:
    /// "command:DeviceType:DeviceId:Cmd[:arg1[:arg2…]]" — sends a HW command
    /// "form:Lib.dll:Namespace.TypeName" — instantiates and ShowDialog()s a Form
    /// </summary>
    [XmlAttribute("action")]
    public string? Action { get; set; }

    /// <summary>Optional license-feature name; item hidden when license lacks it.</summary>
    [XmlAttribute("requires")]
    public string? Requires { get; set; }

    /// <summary>Optional permission name; item hidden unless the current user has it.</summary>
    [XmlAttribute("requiresPermission")]
    public string? RequiresPermission { get; set; }
}
