using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class AbortBlock : BlockBase
{
    public AbortBlock() : base("abort", BlockTheme.HardwareCategory) { }

    public override void BindTo(AstNode node)
    {
        if (node is not AbortStatement a) throw new ArgumentException(nameof(node));
        BoundNode = a;
    }

    public override AstNode ToAst() => new AbortStatement
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("abort", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
