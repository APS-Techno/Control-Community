using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class MfcBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly ParamOrLiteralEditor _flow;

    public MfcBlock() : base("mfc", BlockTheme.HardwareCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _flow = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_flow);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not MfcStatement m) throw new ArgumentException(nameof(node));
        BoundNode = m;
        _device.Text = m.Device;
        _flow.Value = m.Flow;
    }

    public override AstNode ToAst() => new MfcStatement(_device.Text, _flow.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("mfc", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
