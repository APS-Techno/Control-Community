using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class FunctionBlock : ContainerBlockBase, IContainerBlock
{
    private readonly TextBox _name;
    private readonly CsvListField _parameters;

    public BlockStackControl BodyStack => (BlockStackControl)Mouths[0];

    public FunctionBlock() : base("function", BlockTheme.FlowCategory)
    {
        _name = new TextBox { Width = BlockTheme.Scale(100), Margin = new Padding(2) };
        _parameters = new CsvListField();
        FieldRow.Controls.Add(_name);
        FieldRow.Controls.Add(_parameters);
        AddMouth();
    }

    public override void BindTo(AstNode node)
    {
        if (node is not FunctionContainer fn) throw new ArgumentException(nameof(node));
        BoundNode = fn;
        _name.Text = fn.Name;
        _parameters.Value = fn.Parameters;

        BodyStack.Clear();
        foreach (var n in fn.Body)
            BodyStack.Insert(BodyStack.Blocks.Count, BlockFactory.CreateFor(n));

        RestoreCollapsedFromTrivia();
    }

    public override AstNode ToAst()
    {
        var bn = BoundNode as FunctionContainer;
        var b = ImmutableArray.CreateBuilder<AstNode>();
        foreach (var child in BodyStack.Blocks) b.Add(child.ToAst());
        return new FunctionContainer
        {
            Id = BoundNode?.Id ?? NodeId.FromPosition("function", "unbound", 0),
            Body = b.ToImmutable(),
            Name = _name.Text,
            Parameters = _parameters.Value,
            LeadingTrivia = bn?.LeadingTrivia ?? Trivia.Empty,
            TrailingTrivia = bn?.TrailingTrivia ?? Trivia.Empty,
            EndTrivia = bn?.EndTrivia ?? Trivia.Empty,
        };
    }

    IReadOnlyList<BlockStackControl> IContainerBlock.Mouths =>
        new[] { BodyStack };

    IReadOnlyList<System.Collections.Immutable.ImmutableArray<APS.RecipeModel.Ast.AstNode>>
        IContainerBlock.ExtractBodies(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not FunctionContainer fn)
            throw new ArgumentException("expected FunctionContainer", nameof(node));
        return new[] { fn.Body };
    }

    void IContainerBlock.BindHeaderTo(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not FunctionContainer fn) throw new ArgumentException("expected FunctionContainer", nameof(node));
        BoundNode = fn;
        _name.Text = fn.Name;
        _parameters.Value = fn.Parameters;
    }
}
