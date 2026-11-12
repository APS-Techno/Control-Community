namespace APS.SystemControl.Core.Classes;

/// <summary>
/// Pure policy for the recipe "Run" button: given the current override
/// registry state, should the operator be prompted to confirm before
/// starting a recipe, and with what message?
/// </summary>
public static class RecipeRunGate
{
    public static bool ShouldWarn(int activeOverrideCount)
        => activeOverrideCount > 0;

    public static string BuildWarningMessage(int activeOverrideCount)
        => $"{activeOverrideCount} safety override(s) are currently active.\n\n" +
           "Running a recipe with interlocks or alarms disabled is unsafe.\n\n" +
           "Start the recipe anyway?";
}
