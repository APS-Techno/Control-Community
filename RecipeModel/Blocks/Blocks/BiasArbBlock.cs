using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class BiasArbBlock : BlockBase
{
    private readonly TextBox _channel;
    private readonly ParamOrLiteralEditor _voltage;
    private readonly ParamOrLiteralEditor _frequency;
    private readonly ParamOrLiteralEditor _dutyCycle;
    private readonly ParamOrLiteralEditor _phase;

    public BiasArbBlock() : base("biasArb", BlockTheme.PowerCategory)
    {
        _channel = new TextBox { Width = BlockTheme.Scale(50), Margin = new Padding(2) };
        _voltage = new ParamOrLiteralEditor();
        _frequency = new ParamOrLiteralEditor();
        _dutyCycle = new ParamOrLiteralEditor();
        _phase = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_channel);
        FieldRow.Controls.Add(_voltage);
        FieldRow.Controls.Add(_frequency);
        FieldRow.Controls.Add(_dutyCycle);
        FieldRow.Controls.Add(_phase);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not BiasArbStatement b) throw new ArgumentException(nameof(node));
        BoundNode = b;
        _channel.Text = b.Channel;
        _voltage.Value = b.Voltage;
        _frequency.Value = b.Frequency;
        _dutyCycle.Value = b.DutyCycle;
        _phase.Value = b.Phase;
    }

    public override AstNode ToAst() => new BiasArbStatement(
        _channel.Text, _voltage.Value, _frequency.Value, _dutyCycle.Value, _phase.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("biasArb", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
