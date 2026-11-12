using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class GotoBlock : BlockBase
{
    private readonly TextBox _target;

    public GotoBlock() : base("goto", BlockTheme.FlowCategory)
    {
        _target = new TextBox { Width = BlockTheme.Scale(120), Margin = new Padding(2) };
        FieldRow.Controls.Add(_target);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not GotoStatement g) throw new ArgumentException(nameof(node));
        BoundNode = g;
        _target.Text = g.Target;
    }

    public override AstNode ToAst() => new GotoStatement(_target.Text)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("goto", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
