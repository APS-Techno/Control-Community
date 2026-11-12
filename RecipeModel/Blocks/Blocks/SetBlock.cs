using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class SetBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly TextBox _property;
    private readonly ParamOrLiteralEditor _value;

    public SetBlock() : base("set", BlockTheme.PowerCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _property = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _value = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_property);
        FieldRow.Controls.Add(_value);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not SetStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _device.Text = s.Device;
        _property.Text = s.Property;
        _value.Value = s.Value;
    }

    public override AstNode ToAst() => new SetStatement(_device.Text, _property.Text, _value.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("set", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
