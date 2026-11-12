using System.Xml;

namespace APS.SystemControl.Core;

/// <summary>
/// Spatial scale factor for dynamically-loaded HW panels. Forms are FHD-native
/// and not scaled here; WinForms + ApplicationHighDpiMode=SystemAware scales them
/// for OS-level DPI. Fonts are intentionally NOT scaled — shrinking fonts below
/// designer intent makes captions unreadable.
/// </summary>
public static class UIScaling
{
    private const float ReferenceWidth = 2560f;
    private const float AutoMin = 0.5f;
    private const float AutoMax = 1.0f;
    private const float ManualMin = 0.5f;
    private const float ManualMax = 2.0f;

    public static float Current { get; private set; } = 1.0f;

    public static float Compute(float configured) => 1.0f;

    public static void Initialize(float configured)
    {
        Current = Compute(configured);
    }

    /// <summary>
    /// Reads just the &lt;UIScale&gt; element from RootConfig.xml and
    /// initializes Current. Does not fail if the file or element is missing.
    /// </summary>
    public static void InitializeFromRootConfig()
    {
        float configured = 0f;
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RootConfig.xml");
            if (File.Exists(path))
            {
                var doc = new XmlDocument();
                doc.Load(path);
                var node = doc.SelectSingleNode("//UIScale");
                if (node != null && float.TryParse(node.InnerText,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float v))
                {
                    configured = v;
                }
            }
        }
        catch { }
        Initialize(configured);
    }

    /// <summary>
    /// No-op. Forms are FHD-native (designer saved at 1839×979-class sizes);
    /// WinForms AutoScaleMode.Font + ApplicationHighDpiMode=SystemAware handles
    /// per-monitor DPI scaling. Kept as a public shim so existing call sites
    /// compile without edits.
    /// </summary>
    public static void ApplyToForm(Form f) { }

    /// <summary>
    /// Spatially scales a dynamically-loaded panel and its descendants by
    /// UIScaling.Current. Scales Size and Location only — fonts are NOT
    /// scaled, so captions stay readable at designer-intent point sizes.
    /// </summary>
    public static void ApplyToLoadedPanel(Control panel)
    {
        if (Math.Abs(Current - 1.0f) < 0.001f) return;

        panel.SuspendLayout();
        try
        {
            panel.Size = new Size(
                (int)(panel.Size.Width * Current),
                (int)(panel.Size.Height * Current));

            foreach (Control child in panel.Controls)
                ScaleLocationAndSize(child);
        }
        finally { panel.ResumeLayout(true); }
    }

    private static void ScaleLocationAndSize(Control c)
    {
        c.Location = new Point(
            (int)(c.Location.X * Current),
            (int)(c.Location.Y * Current));
        c.Size = new Size(
            (int)(c.Size.Width * Current),
            (int)(c.Size.Height * Current));
        foreach (Control child in c.Controls)
            ScaleLocationAndSize(child);
    }
}
