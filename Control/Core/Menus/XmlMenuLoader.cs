using System.IO;
using System.Xml.Serialization;
using APS.SystemControl.Plugin;
using APS.SystemControl.XML.GUI;

namespace APS.SystemControl.Core.Menus;

/// <summary>
/// Loads Menus.xml from disk and produces one IMenuContributor wrapping all
/// rows. Returns an empty list when the file is absent or unparseable —
/// menu loading must never block app startup.
/// </summary>
internal static class XmlMenuLoader
{
    public static IReadOnlyList<IMenuContributor> Load(string path)
    {
        if (!File.Exists(path)) return [];

        try
        {
            var ser = new XmlSerializer(typeof(XML_Menus));
            using var fs = File.OpenRead(path);
            if (ser.Deserialize(fs) is not XML_Menus root) return [];
            var rows = root.Contributions ?? [];
            if (rows.Count == 0) return [];
            return [new XmlMenuContributor(rows)];
        }
        catch (Exception ex) when (ex is System.Xml.XmlException
                                      or InvalidOperationException
                                      or System.IO.IOException
                                      or UnauthorizedAccessException)
        {
            // Malformed Menus.xml or transient I/O — fall back to no XML-contributed
            // items. (XmlSerializer wraps deserialization errors as InvalidOperationException
            // with an XmlException InnerException.) Any other exception type is unexpected
            // and propagates so it surfaces in the crash log rather than disappearing.
            System.Diagnostics.Debug.WriteLine($"XmlMenuLoader.Load('{path}') failed: {ex.Message}");
            return [];
        }
    }
}
