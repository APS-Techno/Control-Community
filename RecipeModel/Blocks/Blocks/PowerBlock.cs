using APS.Controls.Theme;
using APS.RecipeModel.Ast;

#pragma warning disable WFO1000

namespace APS.RecipeModel.Blocks;

public sealed class PowerBlock : BlockBase
{
    private readonly DarkComboBox _device;
    private readonly DarkComboBox _action;

    // No value editor: `power ID on <level>` is a 2-arg op at runtime and the level is
    // silently discarded, so the block must not let anyone author one. Set the level with
    // a separate `set ID Power <W>` block placed before this one.
    public PowerBlock() : base("power", BlockTheme.PowerCategory)
    {
        _device = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = BlockTheme.Scale(90),
            Margin = new Padding(2),
        };
        _action = new DarkComboBox
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
        if (node is not PowerStatement p) throw new ArgumentException(nameof(node));
        BoundNode = p;
        _device.Text = p.Device;
        _action.SelectedIndex = p.Action == OnOff.On ? 0 : 1;
    }

    public override AstNode ToAst() => new PowerStatement(
        _device.Text,
        _action.SelectedIndex == 1 ? OnOff.Off : OnOff.On,
        // Carry the bound node's legacy value through untouched — editing the device or
        // action of an old `power X on 350` line must not silently rewrite the line.
        (BoundNode as PowerStatement)?.Value ?? NodeField.Literal(""))
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("power", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };

    public override void ApplyContext(IBlockContext? context)
    {
        var devices = context?.DeviceNames("Power Supplies") ?? Array.Empty<string>();
        var prev = _device.Text;
        _device.Items.Clear();
        foreach (var d in devices) _device.Items.Add(d);
        _device.Text = prev;
    }
}
