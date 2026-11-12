using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class LabelBlock : BlockBase
{
    private readonly TextBox _name;

    public LabelBlock() : base("label", BlockTheme.FlowCategory)
    {
        _name = new TextBox { Width = BlockTheme.Scale(120), Margin = new Padding(2) };
        FieldRow.Controls.Add(_name);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not LabelStatement l) throw new ArgumentException(nameof(node));
        BoundNode = l;
        _name.Text = l.Name;
    }

    public override AstNode ToAst() => new LabelStatement(_name.Text)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("label", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
