namespace APS.RecipeModel.Controls.ScriptEditor;

public static class EditorFontSizeHelper
{
    public const float Min = 6f;
    public const float Max = 32f;
    public const float Default = 11f;

    public static float Clamp(float size)
    {
        if (size < Min) return Min;
        if (size > Max) return Max;
        return size;
    }

    public static float Step(float current, int direction)
        => Clamp(current + direction);
}
