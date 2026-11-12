using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class CheckBlock : BlockBase
{
    private readonly TextBox _sensor;
    private readonly APS.Controls.Theme.DarkComboBox _op;
    private readonly ParamOrLiteralEditor _value;

    private static readonly (CompareOp Op, string Text)[] OpMap =
    {
        (CompareOp.Eq, "="),
        (CompareOp.Ne, "!="),
        (CompareOp.Lt, "<"),
        (CompareOp.Le, "<="),
        (CompareOp.Gt, ">"),
        (CompareOp.Ge, ">="),
    };

    public CheckBlock() : base("check", BlockTheme.TimingCategory)
    {
        _sensor = new TextBox { Width = BlockTheme.Scale(60), Margin = new Padding(2) };
        _op = new APS.Controls.Theme.DarkComboBox
        {
            Width = BlockTheme.Scale(50),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(2),
        };
        foreach (var (_, text) in OpMap) _op.Items.Add(text);
        _op.SelectedIndex = 0;

        _value = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_sensor);
        FieldRow.Controls.Add(_op);
        FieldRow.Controls.Add(_value);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not CheckStatement c) throw new ArgumentException(nameof(node));
        BoundNode = c;
        _sensor.Text = c.Sensor;
        int idx = Array.FindIndex(OpMap, m => m.Op == c.Op);
        _op.SelectedIndex = idx >= 0 ? idx : 0;
        _value.Value = c.Value;
    }

    public override AstNode ToAst() => new CheckStatement(
        _sensor.Text, OpMap[_op.SelectedIndex].Op, _value.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("check", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
