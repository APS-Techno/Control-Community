using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class RotationBlock : BlockBase
{
    private readonly APS.Controls.Theme.DarkComboBox _action;
    private readonly CheckBox _hasSpeed;
    private readonly ParamOrLiteralEditor _speed;

    public RotationBlock() : base("rotation", BlockTheme.ProcessCategory)
    {
        _action = new APS.Controls.Theme.DarkComboBox
        {
            Width = BlockTheme.Scale(60),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(2),
        };
        _action.Items.AddRange(new object[] { "on", "off" });
        _action.SelectedIndex = 0;

        _hasSpeed = new CheckBox
        {
            Text = "speed?",
            AutoSize = true,
            Margin = new Padding(4, 4, 2, 2),
            ForeColor = BlockTheme.BlockTextOnCat,
        };
        _speed = new ParamOrLiteralEditor { Enabled = false };
        _hasSpeed.CheckedChanged += (_, _) => _speed.Enabled = _hasSpeed.Checked;

        FieldRow.Controls.Add(_action);
        FieldRow.Controls.Add(_hasSpeed);
        FieldRow.Controls.Add(_speed);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not RotationStatement r) throw new ArgumentException(nameof(node));
        BoundNode = r;
        _action.SelectedIndex = r.Action == OnOff.On ? 0 : 1;
        _hasSpeed.Checked = r.Speed is not null;
        if (r.Speed is { } speed) _speed.Value = speed;
    }

    public override AstNode ToAst() => new RotationStatement(
        _action.SelectedIndex == 1 ? OnOff.Off : OnOff.On,
        _hasSpeed.Checked ? _speed.Value : null)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("rotation", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
