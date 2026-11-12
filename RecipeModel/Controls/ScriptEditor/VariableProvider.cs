using APS.RecipeModel.Model;

namespace APS.RecipeModel.Controls.ScriptEditor;

public readonly record struct VariableItem(string Name, string Type, string DefaultValue);

public static class VariableProvider
{
    public static readonly VariableItem LoopIndex = new("_i", "int", "loop index");

    public static IEnumerable<VariableItem> GetAll(IEnumerable<RecipeParam> parameters)
    {
        foreach (var p in parameters)
            yield return new VariableItem(p.Name, ParamTypeToString(p.ParamType), p.Value);
        yield return LoopIndex;
    }

    public static IEnumerable<VariableItem> Filter(IEnumerable<VariableItem> items, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) return items;
        return items.Where(v => v.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public static string Format(VariableItem item)
        => $"${item.Name}  :{item.Type}  — {item.DefaultValue}";

    private static string ParamTypeToString(ParamType t) => t switch
    {
        ParamType.Float  => "float",
        ParamType.Int    => "int",
        ParamType.String => "string",
        ParamType.Bool   => "bool",
        _ => t.ToString().ToLowerInvariant()
    };
}
