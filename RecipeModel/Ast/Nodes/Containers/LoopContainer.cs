namespace APS.RecipeModel.Ast;

public sealed record LoopContainer : Container
{
    public required NodeField Count { get; init; }
    public Trivia EndTrivia { get; init; } = Trivia.Empty;
}
