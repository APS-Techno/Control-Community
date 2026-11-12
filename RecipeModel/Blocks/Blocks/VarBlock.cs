using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class VarBlock : BlockBase
{
    private readonly TextBox _name;
    private readonly ParamOrLiteralEditor _value;

    public VarBlock() : base("var", BlockTheme.SubroutineCategory)
    {
        _name = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _value = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_name);
        FieldRow.Controls.Add(_value);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not VarStatement v) throw new ArgumentException(nameof(node));
        BoundNode = v;
        _name.Text = v.Name;
        _value.Value = v.Value;
    }

    public override AstNode ToAst() => new VarStatement(_name.Text, _value.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("var", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
