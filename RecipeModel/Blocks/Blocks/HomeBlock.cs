using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class HomeBlock : BlockBase
{
    public HomeBlock() : base("home", BlockTheme.HardwareCategory) { }

    public override void BindTo(AstNode node)
    {
        if (node is not HomeStatement h) throw new ArgumentException(nameof(node));
        BoundNode = h;
    }

    public override AstNode ToAst() => new HomeStatement
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("home", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
