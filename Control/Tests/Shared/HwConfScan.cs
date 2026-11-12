using System.Xml.Linq;

namespace APS.SystemControl.Tests.Shared;

public static class HwConfScan
{
    public static IReadOnlyList<string> TypeNames(string hwConfPath)
        => XDocument.Load(hwConfPath).Descendants()
            .SelectMany(e => e.Attributes())
            .Where(a => a.Name.LocalName is "type" or "hwtype")
            .Select(a => a.Value)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
