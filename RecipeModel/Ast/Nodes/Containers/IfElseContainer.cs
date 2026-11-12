using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public sealed record IfElseContainer : Container
{
    public required ConditionExpr Condition { get; init; }
    /// <summary>Non-null if the recipe has an `else` arm; empty array allowed.</summary>
    public ImmutableArray<AstNode>? ElseBody { get; init; }
    /// <summary>Trivia attached to the `else` keyword itself (comments on that line).</summary>
    public Trivia ElseTrivia { get; init; } = Trivia.Empty;
    public Trivia EndTrivia { get; init; } = Trivia.Empty;
}
