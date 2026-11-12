namespace APS.RecipeModel.Validation;

/// <summary>
/// A recipe variable; its default value may be overridden by recipe parameters
/// from the selector UI.
/// </summary>
public class RecipeVariable
{
    /// <summary>Without the leading '$'.</summary>
    public string Name { get; set; } = "";

    /// <summary>"float", "int", "string", or "bool".</summary>
    public string Type { get; set; } = "float";

    public object? Value { get; set; }

    /// <summary>As declared in the recipe text, before parameter overrides.</summary>
    public object? DefaultValue { get; set; }
}
