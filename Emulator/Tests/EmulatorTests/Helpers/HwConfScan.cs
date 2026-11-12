using System.Xml.Linq;

namespace EmulatorTests;

public static class HwConfScan
{
    public static IReadOnlyCollection<string> ShortTypes(string hwConfPath)
    {
        var result = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in XDocument.Load(hwConfPath).Root!.Elements())
        {
            if (section.Name.LocalName == "robots")
            {
                result.Add("robots");
                continue;
            }
            string hwtype = section.Attribute("hwtype")?.Value ?? "";
            if (hwtype.Length == 0) continue;
            int dot = hwtype.LastIndexOf('.');
            result.Add(dot >= 0 ? hwtype[(dot + 1)..] : hwtype);
        }
        return result;
    }
}
