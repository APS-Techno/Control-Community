using APS.RecipeModel.Serialization;

namespace APS.SystemControl.Core.Evidence;

/// <summary>
/// Resolves the *effective* recipe (operator parameter edits baked in) as embeddable text.
/// .rcpx → load, apply overrides, re-serialize (via a temp file, reusing RecipeSerializer).
/// Anything else (legacy .recipe DSL) → raw file text, verbatim.
/// </summary>
public static class EffectiveRecipe
{
    public static (string Name, string Format, string Content) Resolve(
        string recipePath, IReadOnlyDictionary<string, string>? overrides)
    {
        string name = Path.GetFileNameWithoutExtension(recipePath);
        string ext = Path.GetExtension(recipePath);

        if (ext.Equals(".rcpx", StringComparison.OrdinalIgnoreCase))
        {
            var recipe = RecipeSerializer.Load(recipePath);
            recipe.ApplyParameterOverrides(overrides ?? new Dictionary<string, string>());

            string tmp = Path.Combine(Path.GetTempPath(), $"aps-eff-{Guid.NewGuid():N}.rcpx");
            try
            {
                RecipeSerializer.Save(recipe, tmp);
                return (name, "rcpx", File.ReadAllText(tmp));
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        return (name, ext.TrimStart('.').ToLowerInvariant(), File.ReadAllText(recipePath));
    }
}
