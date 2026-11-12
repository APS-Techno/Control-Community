namespace APS.RecipeModel.Ast;

public abstract record AstNode
{
    public required NodeId Id { get; init; }
    public Trivia LeadingTrivia { get; init; } = Trivia.Empty;
    public Trivia TrailingTrivia { get; init; } = Trivia.Empty;
    public SourceSpan Span { get; init; } = SourceSpan.None;
}
