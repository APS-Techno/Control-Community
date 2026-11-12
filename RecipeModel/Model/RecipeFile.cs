using System.Text;

namespace APS.RecipeModel.Model;

public class RecipeFile
{
    public RecipeDescription Description { get; set; } = new();
    public List<ParamGroup> Parameters { get; set; } = [];
    public string Script { get; set; } = "";

    /// <summary>
    /// Matching is by parameter <see cref="RecipeParam.Name"/>, case-insensitive;
    /// unknown keys are ignored and unmatched params keep their defaults.
    /// This mutates the loaded copy only — the source .rcpx file is never changed.
    /// </summary>
    public void ApplyParameterOverrides(IReadOnlyDictionary<string, string> overrides)
    {
        if (overrides is null || overrides.Count == 0) return;

        var lookup = overrides as Dictionary<string, string> is { } d
            && d.Comparer == StringComparer.OrdinalIgnoreCase
            ? d
            : new Dictionary<string, string>(overrides, StringComparer.OrdinalIgnoreCase);

        foreach (var group in Parameters)
            foreach (var p in group.Params)
                if (lookup.TryGetValue(p.Name, out var value))
                    p.Value = value;
    }

    public string GenerateVarBlock()
    {
        var sb = new StringBuilder();
        foreach (var group in Parameters)
        {
            foreach (var p in group.Params)
            {
                sb.AppendLine($"var {p.Name} {p.Value}");
            }
        }
        return sb.ToString();
    }

    public string FullScript => GenerateVarBlock() + Script;
}
