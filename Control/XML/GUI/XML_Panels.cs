using System.Xml.Serialization;

namespace APS.SystemControl.XML.GUI;

[XmlRoot(ElementName = "panels")]
public class XML_Panels
{
    [XmlElement(ElementName = "panel")]
    public List <XML_Panel>? Panels { get; set; }
}

public class XML_Panel
{
    [XmlElement(ElementName = "id")]
    public string? ID { get; set; }

    [XmlElement(ElementName = "caption")]
    public string? Caption { get; set; }

    [XmlElement(ElementName = "lib")]
    public string? Lib { get; set; }

    [XmlElement(ElementName = "type")]
    public string? Type { get; set; }

    [XmlElement(ElementName = "event")]
    public string? Event { get; set; }

    [XmlElement(ElementName = "msghandler")]
    public string? MsgHandler { get; set; }

    [XmlElement(ElementName = "parent")]
    public string? Parent { get; set; }

    [XmlElement(ElementName = "col")]
    public int Col { get; set; }

    [XmlElement(ElementName = "row")]
    public int Row { get; set; }
    [XmlElement(ElementName = "left")]
    public float Left { get; set; }

    [XmlElement(ElementName = "top")]
    public float Top { get; set; }

    /// <summary>
    /// Optional per-panel background color as an HTML/CSS hex string
    /// (e.g. "#16323F"). Applied by <c>LoadPanel</c> after theming so the panel
    /// lifts off the column/schematic background. Absent or blank leaves the
    /// panel at its default dark theme shade.
    /// </summary>
    [XmlElement(ElementName = "color")]
    public string? Color { get; set; }

    /// <summary>
    /// Optional header-bar color as an HTML/CSS hex string. When absent the
    /// header auto-derives from the background (<c>DarkenColor(BackColor, 0.80)</c>).
    /// </summary>
    [XmlElement(ElementName = "headerColor")]
    public string? HeaderColor { get; set; }

    /// <summary>
    /// Optional panel-border color as an HTML/CSS hex string. When absent the
    /// panel uses its default border (<c>#283C5A</c>).
    /// </summary>
    [XmlElement(ElementName = "borderColor")]
    public string? BorderColor { get; set; }

    /// <summary>
    /// Optional button-face color as an HTML/CSS hex string, applied to every
    /// <c>LedButton</c> in the panel. When absent buttons use the global theme
    /// face color (<c>VacuumTheme.MetalDark</c>).
    /// </summary>
    [XmlElement(ElementName = "buttonColor")]
    public string? ButtonColor { get; set; }

    /// <summary>
    /// Optional drive-icon sweep speed in seconds per adjacent flange, applied to
    /// panels implementing <c>IAnimatedSchematicPanel</c> (StepperDrivePanel,
    /// other process panels). Base value for BOTH holder and shutter; the per-drive
    /// elements below override it. System-dependent: calibrate to the real drive's
    /// traverse time. 0 (absent) keeps the panel's built-in default.
    /// </summary>
    [XmlElement(ElementName = "animFlangeSeconds")]
    public float AnimFlangeSeconds { get; set; }

    /// <summary>Optional holder-icon sweep speed (seconds per adjacent flange).
    /// Overrides <see cref="AnimFlangeSeconds"/> for the holder only — the two
    /// motors run at different velocities.</summary>
    [XmlElement(ElementName = "animHolderSeconds")]
    public float AnimHolderSeconds { get; set; }

    /// <summary>Optional shutter-icon sweep speed (seconds per adjacent flange).
    /// Overrides <see cref="AnimFlangeSeconds"/> for the shutter only.</summary>
    [XmlElement(ElementName = "animShutterSeconds")]
    public float AnimShutterSeconds { get; set; }

    // XmlSerializer hooks: keep the anim elements out of panels that never set
    // them when PanelDragManager re-saves the file after a drag.
    public bool ShouldSerializeAnimFlangeSeconds() => AnimFlangeSeconds > 0;
    public bool ShouldSerializeAnimHolderSeconds() => AnimHolderSeconds > 0;
    public bool ShouldSerializeAnimShutterSeconds() => AnimShutterSeconds > 0;

    [XmlText]
    public string? Value { get; set; }
}
