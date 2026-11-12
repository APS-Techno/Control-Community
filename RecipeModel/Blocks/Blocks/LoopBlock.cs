using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed class LoopBlock : ContainerBlockBase, IContainerBlock
{
    private readonly ParamOrLiteralEditor _count;

    public BlockStackControl BodyStack => (BlockStackControl)Mouths[0];

    public LoopBlock() : base("loop", BlockTheme.FlowCategory)
    {
        _count = new ParamOrLiteralEditor();
        FieldRow.Controls.Add(_count);
        AddMouth();
    }

    public override void BindTo(AstNode node)
    {
        if (node is not LoopContainer lp) throw new ArgumentException("expected LoopContainer", nameof(node));
        BoundNode = lp;
        _count.Value = lp.Count;

        BodyStack.Clear();
        foreach (var child in lp.Body)
            BodyStack.Insert(BodyStack.Blocks.Count, BlockFactory.CreateFor(child));

        RestoreCollapsedFromTrivia();
    }

    public override AstNode ToAst()
    {
        var id = BoundNode?.Id ?? NodeId.FromPosition("loop", "unbound", 0);
        var leading = BoundNode?.LeadingTrivia ?? Trivia.Empty;
        var trailing = BoundNode?.TrailingTrivia ?? Trivia.Empty;
        var end = (BoundNode as LoopContainer)?.EndTrivia ?? Trivia.Empty;

        var childBuilder = ImmutableArray.CreateBuilder<AstNode>();
        foreach (var b in BodyStack.Blocks) childBuilder.Add(b.ToAst());

        return new LoopContainer
        {
            Id = id,
            Body = childBuilder.ToImmutable(),
            Count = _count.Value,
            LeadingTrivia = leading,
            TrailingTrivia = trailing,
            EndTrivia = end,
        };
    }

    internal NodeField CountForTesting => _count.Value;
    internal void SetCountForTesting(NodeField v) => _count.Value = v;

    IReadOnlyList<BlockStackControl> IContainerBlock.Mouths =>
        new[] { BodyStack };

    IReadOnlyList<System.Collections.Immutable.ImmutableArray<APS.RecipeModel.Ast.AstNode>>
        IContainerBlock.ExtractBodies(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not LoopContainer lp)
            throw new ArgumentException("expected LoopContainer", nameof(node));
        return new[] { lp.Body };
    }

    void IContainerBlock.BindHeaderTo(APS.RecipeModel.Ast.AstNode node)
    {
        if (node is not LoopContainer lp) throw new ArgumentException("expected LoopContainer", nameof(node));
        BoundNode = lp;
        _count.Value = lp.Count;
    }
}
