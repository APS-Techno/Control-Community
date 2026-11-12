using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class ValveBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly APS.Controls.Theme.DarkComboBox _action;

    public ValveBlock() : base("valve", BlockTheme.HardwareCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _action = new APS.Controls.Theme.DarkComboBox
        {
            Width = BlockTheme.Scale(80),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(2),
        };
        _action.Items.AddRange(new object[] { "open", "close" });
        _action.SelectedIndex = 0;

        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_action);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not ValveStatement v) throw new ArgumentException("expected ValveStatement", nameof(node));
        BoundNode = v;
        _device.Text = v.Device;
        _action.SelectedIndex = v.Action == ValveAction.Open ? 0 : 1;
    }

    public override AstNode ToAst()
    {
        var action = _action.SelectedIndex == 1 ? ValveAction.Close : ValveAction.Open;
        var id = BoundNode?.Id ?? NodeId.FromPosition("valve", "unbound", 0);
        var leading = BoundNode?.LeadingTrivia ?? Trivia.Empty;
        var trailing = BoundNode?.TrailingTrivia ?? Trivia.Empty;
        return new ValveStatement(_device.Text, action)
        {
            Id = id,
            LeadingTrivia = leading,
            TrailingTrivia = trailing,
        };
    }

    internal void SetDeviceForTesting(string device) => _device.Text = device;
}
