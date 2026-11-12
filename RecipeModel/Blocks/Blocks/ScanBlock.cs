using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class ScanBlock : BlockBase
{
    private readonly TextBox _device;
    private readonly ParamOrLiteralEditor _start;
    private readonly ParamOrLiteralEditor _end;
    private readonly ParamOrLiteralEditor _speed;

    public ScanBlock() : base("scan", BlockTheme.HardwareCategory)
    {
        _device = new TextBox { Width = BlockTheme.Scale(80), Margin = new Padding(2) };
        _start = new ParamOrLiteralEditor();
        _end = new ParamOrLiteralEditor();
        _speed = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_device);
        FieldRow.Controls.Add(_start);
        FieldRow.Controls.Add(_end);
        FieldRow.Controls.Add(_speed);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not ScanStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _device.Text = s.Device;
        _start.Value = s.Start;
        _end.Value = s.End;
        _speed.Value = s.Speed;
    }

    public override AstNode ToAst() => new ScanStatement(_device.Text, _start.Value, _end.Value, _speed.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("scan", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
