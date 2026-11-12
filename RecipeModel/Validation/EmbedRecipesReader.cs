using System.Xml.Linq;

namespace APS.RecipeModel.Validation;

public static class EmbedRecipesReader
{
    /// <summary>
    /// Returns an empty list if the file doesn't exist or can't be parsed.
    /// </summary>
    public static List<string> ReadRecipeNames(string embedRecipesPath)
    {
        var result = new List<string>();

        if (!File.Exists(embedRecipesPath))
            return result;

        try
        {
            var doc = XDocument.Load(embedRecipesPath);
            var root = doc.Element("recipes");
            if (root is null) return result;

            foreach (var recipe in root.Elements("recipe"))
            {
                string? id = recipe.Attribute("id")?.Value;
                if (!string.IsNullOrWhiteSpace(id))
                    result.Add(id);
            }
        }
        catch
        {
            // If the file can't be read, return empty — validation will skip the check
        }

        return result;
    }
}
