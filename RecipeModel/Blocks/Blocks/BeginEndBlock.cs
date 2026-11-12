using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class BeginEndBlock : ContainerBlockBase, IContainerBlock
{
    public BlockStackControl BodyStack => (BlockStackControl)Mouths[0];

    public BeginEndBlock() : base("begin", BlockTheme.StructureCategory)
    {
        AddMouth();
    }

    public override void BindTo(AstNode node)
    {
        if (node is not BeginEndContainer be) throw new ArgumentException(nameof(node));
        BoundNode = be;
        BodyStack.Clear();
        foreach (var n in be.Body)
            BodyStack.Insert(BodyStack.Blocks.Count, BlockFactory.CreateFor(n));

        RestoreCollapsedFromTrivia();
    }

    public override AstNode ToAst()
    {
        var bn = BoundNode as BeginEndContainer;
        var b = ImmutableArray.CreateBuilder<AstNode>();
        foreach (var child in BodyStack.Blocks) b.Add(child.ToAst());
        return new BeginEndContainer
        {
            Id = BoundNode?.Id ?? NodeId.FromPosition("begin", "unbound", 0),
            Body = b.ToImmutable(),
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
        if (node is not BeginEndContainer be)
            throw new ArgumentException("expected BeginEndContainer", nameof(node));
        return new[] { be.Body };
    }

    void IContainerBlock.BindHeaderTo(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not BeginEndContainer be) throw new ArgumentException("expected BeginEndContainer", nameof(node));
        BoundNode = be;
    }
}
