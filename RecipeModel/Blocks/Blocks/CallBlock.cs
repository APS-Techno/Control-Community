using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class CallBlock : BlockBase
{
    private readonly TextBox _recipe;

    public CallBlock() : base("call", BlockTheme.SubroutineCategory)
    {
        _recipe = new TextBox { Width = BlockTheme.Scale(160), Margin = new Padding(2) };
        FieldRow.Controls.Add(_recipe);
    }

    public override void BindTo(AstNode node)
    {
        if (node is not CallStatement c) throw new ArgumentException(nameof(node));
        BoundNode = c;
        _recipe.Text = c.RecipeName;
    }

    public override AstNode ToAst() => new CallStatement(_recipe.Text)
    {
        Id = BoundNode?.Id ?? NodeId.FromPosition("call", "unbound", 0),
        LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
        TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
    };
}
