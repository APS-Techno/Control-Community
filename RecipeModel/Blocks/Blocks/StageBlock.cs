using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class StageBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly ParamOrLiteralEditor _position;

    public StageBlock() : base("stage", BlockTheme.HardwareCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _position = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_position);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not StageStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _device.Text = s.Device;
        _position.Value = s.Position;
    }

    public override AstNode ToAst() => new StageStatement(_device.Text, _position.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("stage", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
