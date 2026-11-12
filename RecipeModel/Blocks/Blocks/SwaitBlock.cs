using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class SwaitBlock : BlockBase
{
    private readonly TextBox _sensor;
    private readonly ParamOrLiteralEditor _target;
    private readonly ParamOrLiteralEditor _timeout;

    public SwaitBlock() : base("swait", BlockTheme.TimingCategory)
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
        if (node is not SwaitStatement s) throw new ArgumentException(nameof(node));
        BoundNode = s;
        _sensor.Text = s.Sensor;
        _target.Value = s.Target;
        _timeout.Value = s.TimeoutSec;
    }

    public override AstNode ToAst() => new SwaitStatement(
        _sensor.Text, _target.Value, _timeout.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("swait", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
