namespace APS.RecipeModel.Ast;

/// <summary>A `begin` ... `end` block (no-op marker, but may contain statements).</summary>
public sealed record BeginEndContainer : Container
{
    public Trivia EndTrivia { get; init; } = Trivia.Empty;
}
