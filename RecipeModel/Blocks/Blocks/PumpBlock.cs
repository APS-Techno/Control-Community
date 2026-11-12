using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class PumpBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly APS.Controls.Theme.DarkComboBox _action;

    public PumpBlock() : base("pump", BlockTheme.HardwareCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _action = new APS.Controls.Theme.DarkComboBox
        {
            Width = BlockTheme.Scale(60),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(2),
        };
        _action.Items.AddRange(new object[] { "on", "off" });
        _action.SelectedIndex = 0;
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_action);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not PumpStatement p) throw new ArgumentException(nameof(node));
        BoundNode = p;
        _device.Text = p.Device;
        _action.SelectedIndex = p.Action == OnOff.On ? 0 : 1;
    }

    public override AstNode ToAst() => new PumpStatement(
        _device.Text,
        _action.SelectedIndex == 1 ? OnOff.Off : OnOff.On)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("pump", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
