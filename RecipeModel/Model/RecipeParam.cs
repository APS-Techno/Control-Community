namespace APS.RecipeModel.Model;

public class RecipeParam
{
    public string Name { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Unit { get; set; } = "";
    public ParamType ParamType { get; set; } = ParamType.Float;
    public string Value { get; set; } = "";
}
