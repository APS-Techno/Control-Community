using System.Xml.Linq;

namespace APS.RecipeModel.Validation;

/// <summary>
/// Reads resolvable source names from a <c>Sources.xml</c> file.
/// Returns distinct effective names (name attribute, else material) in
/// document order, EXCLUDING any handle a recipe worker would treat as
/// ambiguous (see <see cref="SourceNames.Resolvable"/>) — dropdowns and
/// validators must never offer a handle that later throws at
/// <c>Operations.ResolvePosition</c>. Missing or malformed files yield an
/// empty list (best-effort, never throws).
/// </summary>
public static class SourcesReader
{
    public static IReadOnlyList<string> Read(string sourcesXmlPath)
    {
        if (!File.Exists(sourcesXmlPath))
            return Array.Empty<string>();

        try
        {
            var doc = XDocument.Load(sourcesXmlPath);
            var sources = new List<(string? name, string? material)>();
            foreach (var el in doc.Descendants("source"))
                sources.Add((el.Attribute("name")?.Value, el.Element("material")?.Value));
            return SourceNames.Resolvable(sources);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
