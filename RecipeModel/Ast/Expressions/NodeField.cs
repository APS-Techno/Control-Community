namespace APS.RecipeModel.Ast;

public sealed record NodeField(string Value, bool IsParamRef)
{
    public static NodeField Literal(string v) => new(v, false);
    public static NodeField Param(string name)
        => new(name.StartsWith('$') ? name[1..] : name, true);

    public string ToDslToken() => IsParamRef ? $"${Value}" : Value;
}
