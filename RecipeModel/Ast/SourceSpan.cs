namespace APS.RecipeModel.Ast;

/// <summary>1-based Line, 0-based Column, Length in characters.</summary>
public readonly record struct SourceSpan(int Line, int Column, int Length)
{
    public static SourceSpan None { get; } = new(0, 0, 0);
}
