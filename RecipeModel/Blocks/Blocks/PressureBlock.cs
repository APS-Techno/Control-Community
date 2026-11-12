using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class PressureBlock : BlockBase
{
    private readonly TextBox _sensor;
    private readonly ParamOrLiteralEditor _target;
    private readonly ParamOrLiteralEditor _timeout;

    public PressureBlock() : base("pwait", BlockTheme.TimingCategory)
    {
        _sensor = new TextBox { Width = BlockTheme.Scale(60), Margin = new Padding(2) };
        _target = new ParamOrLiteralEditor();
        _timeout = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_sensor);
        FieldRow.Controls.Add(_target);
        FieldRow.Controls.Add(_timeout);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not PressureStatement p) throw new ArgumentException(nameof(node));
        BoundNode = p;
        _sensor.Text = p.Sensor;
        _target.Value = p.TargetMTorr;
        _timeout.Value = p.TimeoutSec;
    }

    public override AstNode ToAst() => new PressureStatement(
        _sensor.Text, _target.Value, _timeout.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("pressure", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
