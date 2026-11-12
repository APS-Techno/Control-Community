using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class MessageBlock : BlockBase
{
    private readonly TextBox _text;

    public MessageBlock() : base("message", BlockTheme.HardwareCategory)
    {
        _text = new TextBox { Width = BlockTheme.Scale(240), Margin = new Padding(2) };
        FieldRow.Controls.Add(_text);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not MessageStatement m) throw new ArgumentException(nameof(node));
        BoundNode = m;
        _text.Text = m.Text;
    }

    public override AstNode ToAst() => new MessageStatement(_text.Text)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("message", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
