using System.Xml.Serialization;

namespace APS.SystemControl.Core;

/// <summary>
/// Per-user UI layout state that persists across sessions: side splitter
/// positions on the System tab and the Process panel, plus the floating
/// events log overlay bounds and visibility. Stored as XML in
/// <c>%APPDATA%\APS\System\layout.xml</c>.
/// </summary>
public class UserLayout
{
    public int ProcessPanelLeftWidth { get; set; }
    public int ProcessPanelRightWidth { get; set; }
    public int ProcessPanelCenterWidth { get; set; }
    public int SystemTabLeftWidth { get; set; }
    public int SystemTabRightWidth { get; set; }

    // Floating events log overlay on the System tab (vacuumSchematic).
    // Sentinel -1 on X/Y means "use default position" (top-left, 12 px inset).
    // Nullable bool so first-run users get the default-true behavior even if
    // the existing layout.xml lacks the element.
    public int SystemEventsLogX { get; set; } = -1;
    public int SystemEventsLogY { get; set; } = -1;
    public int SystemEventsLogWidth { get; set; } = 360;
    public int SystemEventsLogHeight { get; set; } = 220;
    public bool? SystemEventsLogVisible { get; set; }

    // PSU panel dynamic-visibility toggles. Non-nullable with default-true initializers;
    // XmlSerializer runs the default ctor first, so a layout.xml predating this feature
    // (missing the elements) keeps these true.
    public bool HideInactiveSupplies { get; set; } = true;
    public bool AutoAlignOnHide { get; set; } = true;

    [XmlArrayItem("Profile")]
    public List<ProfileScaleEntry> SymbolScales { get; set; } = [];

    public float? GetSymbolScale(string profile) =>
        SymbolScales.Find(e => string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase))?.Scale;

    public void SetSymbolScale(string profile, float scale)
    {
        RemoveSymbolScale(profile);
        SymbolScales.Add(new ProfileScaleEntry { Profile = profile, Scale = scale });
    }

    public void RemoveSymbolScale(string profile) =>
        SymbolScales.RemoveAll(e => string.Equals(e.Profile, profile, StringComparison.OrdinalIgnoreCase));

    private static string LayoutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "APS", "System", "layout.xml");

    public static UserLayout Load()
    {
        try
        {
            string path = LayoutPath;
            if (!File.Exists(path)) return new UserLayout();
            using var reader = new StreamReader(path);
            var ser = new XmlSerializer(typeof(UserLayout));
            return (UserLayout?)ser.Deserialize(reader) ?? new UserLayout();
        }
        catch
        {
            return new UserLayout();
        }
    }

    public void Save()
    {
        try
        {
            string path = LayoutPath;
            string dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            using var writer = new StreamWriter(path);
            var ser = new XmlSerializer(typeof(UserLayout));
            ser.Serialize(writer, this);
        }
        catch
        {
            // best-effort — never crash on layout save
        }
    }
}

public class ProfileScaleEntry
{
    [XmlAttribute("profile")] public string Profile { get; set; } = "";
    [XmlAttribute("scale")] public float Scale { get; set; }
}
