namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Whether the running recipe's detailed logging (TSV data file + step-log
/// stream) is enabled, given the recipe class and the two per-class toggles.
/// </summary>
public static class RecipeLogPolicy
{
    public static bool ShouldLog(bool isUserRecipe, bool logUserRecipes, bool logEmbeddedRecipes)
        => isUserRecipe ? logUserRecipes : logEmbeddedRecipes;
}
