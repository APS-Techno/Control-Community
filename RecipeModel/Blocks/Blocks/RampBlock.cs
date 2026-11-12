using APS.Controls.Theme;
using APS.RecipeModel.Ast;

#pragma warning disable WFO1000

namespace APS.RecipeModel.Blocks;

public sealed class RampBlock : BlockBase
{
    private readonly DarkComboBox _device;
    private readonly ParamOrLiteralEditor _value;

    public RampBlock() : base("ramp", BlockTheme.PowerCategory)
    {
        _device = new DarkComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDown,
            Width = BlockTheme.Scale(90),
            Margin = new Padding(2),
        };
        _value = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_value);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not RampStatement r) throw new ArgumentException(nameof(node));
        BoundNode = r;
        _device.Text = r.Device;
        _value.Value = r.Value;
    }

    public override AstNode ToAst() => new RampStatement(_device.Text, _value.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("ramp", "unbound", 0),
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
        _value.SetAvailableParams(context?.ParameterNames ?? Array.Empty<string>());
    }
}
