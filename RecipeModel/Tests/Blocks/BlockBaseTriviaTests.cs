using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockBaseTriviaTests
{
    // Mirrors LabelBlock's BindTo/ToAst pattern: BoundNode is the source of truth
    // for Id/trivia; ToAst fabricates a fresh Id when never bound.
    private sealed class FakeBlock : BlockBase
    {
        public FakeBlock() : base("fake", BlockTheme.StructureCategory) { }

        public override void BindTo(AstNode node) => BoundNode = node;

        public override AstNode ToAst() => new LabelStatement("x")
        {
            Id = BoundNode?.Id ?? NodeId.FromPosition("fake", "unbound", 0),
            LeadingTrivia = BoundNode?.LeadingTrivia ?? Trivia.Empty,
            TrailingTrivia = BoundNode?.TrailingTrivia ?? Trivia.Empty,
        };
    }

    [StaFact]
    public void SetLeadingTrivia_On_Bound_Block_Preserves_Id_And_Updates_Trivia()
    {
        var id = NodeId.FromPosition("l", "t", 0);
        var block = new FakeBlock();
        block.BindTo(new LabelStatement("x") { Id = id });

        var trivia = RegionTrivia.PrependOpen(Trivia.Empty, "R", false);
        block.SetLeadingTrivia(trivia);

        Assert.Equal(id, block.ToAst().Id);
        Assert.Equal(trivia, block.LeadingTrivia);
    }

    [StaFact]
    public void SetTrailingTrivia_On_Bound_Block_Preserves_Id_And_Updates_Trivia()
    {
        var id = NodeId.FromPosition("l", "t", 1);
        var block = new FakeBlock();
        block.BindTo(new LabelStatement("x") { Id = id });

        var trivia = RegionTrivia.PrependClose(Trivia.Empty);
        block.SetTrailingTrivia(trivia);

        Assert.Equal(id, block.ToAst().Id);
        Assert.Equal(trivia, block.TrailingTrivia);
    }

    [StaFact]
    public void SetLeadingTrivia_On_Unbound_Block_Fabricates_BoundNode_From_ToAst()
    {
        var block = new FakeBlock();

        var trivia = RegionTrivia.PrependOpen(Trivia.Empty, "R", false);
        block.SetLeadingTrivia(trivia);

        Assert.Equal(trivia, block.LeadingTrivia);
    }

    [StaFact]
    public void SetTrailingTrivia_On_Unbound_Block_Fabricates_BoundNode_From_ToAst()
    {
        var block = new FakeBlock();

        var trivia = RegionTrivia.PrependClose(Trivia.Empty);
        block.SetTrailingTrivia(trivia);

        Assert.Equal(trivia, block.TrailingTrivia);
    }
}
