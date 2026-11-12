using System.Collections.Immutable;

namespace APS.RecipeModel.Ast;

public sealed record FunctionContainer : Container
{
    public required string Name { get; init; }
    public required ImmutableArray<string> Parameters { get; init; }
    public Trivia EndTrivia { get; init; } = Trivia.Empty;
}
