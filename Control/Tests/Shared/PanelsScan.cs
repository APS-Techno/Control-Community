using System.Xml.Linq;

namespace APS.SystemControl.Tests.Shared;

public static class PanelsScan
{
    public static IReadOnlyList<string> Types(string panelsPath)
        => XDocument.Load(panelsPath).Descendants("panel")
            .Select(p => p.Element("type")?.Value)
            .Where(t => !string.IsNullOrEmpty(t))
            .Select(t => t!)
            .Distinct()
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
