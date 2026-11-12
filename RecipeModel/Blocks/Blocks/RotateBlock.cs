using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class RotateBlock : BlockBase
{
    private readonly ParamOrLiteralEditor _speed;

    public RotateBlock() : base("rotate", BlockTheme.ProcessCategory)
    {
        _speed = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_speed);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not RotateStatement r) throw new ArgumentException(nameof(node));
        BoundNode = r;
        _speed.Value = r.Speed;
    }

    public override AstNode ToAst() => new RotateStatement(_speed.Value)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("rotate", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
