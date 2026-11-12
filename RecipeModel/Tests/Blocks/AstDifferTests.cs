using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class AstDifferTests
{
    private static ValveStatement V(int i) =>
        new("M1", ValveAction.Open) { Id = NodeId.FromPosition("valve", "t", i) };
    private static MfcStatement M(int i) =>
        new("Ar1", NodeField.Literal("5")) { Id = NodeId.FromPosition("mfc", "t", i) };

    [Fact]
    public void Equal_Counts_Same_Kinds_All_BindInPlace()
    {
        var oldB = ImmutableArray.Create<AstNode>(V(0), V(1));
        var newB = ImmutableArray.Create<AstNode>(V(0), V(1));
        var ops = AstDiffer.DiffBody(oldB, newB);
        Assert.Equal(2, ops.Count);
        Assert.All(ops, op => Assert.IsType<BindInPlaceOp>(op));
    }

    [Fact]
    public void Longer_New_Appends_Insert_Ops()
    {
        var oldB = ImmutableArray.Create<AstNode>(V(0));
        var newB = ImmutableArray.Create<AstNode>(V(0), M(1));
        var ops = AstDiffer.DiffBody(oldB, newB);
        Assert.Equal(2, ops.Count);
        Assert.IsType<BindInPlaceOp>(ops[0]);
        var ins = Assert.IsType<InsertOp>(ops[1]);
        Assert.Equal(1, ins.Index);
    }

    [Fact]
    public void Shorter_New_Emits_Remove_Ops()
    {
        var oldB = ImmutableArray.Create<AstNode>(V(0), V(1));
        var newB = ImmutableArray.Create<AstNode>(V(0));
        var ops = AstDiffer.DiffBody(oldB, newB);
        Assert.Equal(2, ops.Count);
        Assert.IsType<BindInPlaceOp>(ops[0]);
        var rem = Assert.IsType<RemoveOp>(ops[1]);
        Assert.Equal(1, rem.Index);
    }

    [Fact]
    public void Kind_Mismatch_Produces_Replace_Op()
    {
        var oldB = ImmutableArray.Create<AstNode>(V(0));
        var newB = ImmutableArray.Create<AstNode>(M(0));
        var ops = AstDiffer.DiffBody(oldB, newB);
        var rep = Assert.IsType<ReplaceOp>(Assert.Single(ops));
        Assert.Equal(0, rep.Index);
        Assert.IsType<MfcStatement>(rep.NewNode);
    }

    [Fact]
    public void Empty_Both_Sides_Produces_No_Ops()
    {
        var ops = AstDiffer.DiffBody(
            ImmutableArray<AstNode>.Empty,
            ImmutableArray<AstNode>.Empty);
        Assert.Empty(ops);
    }
}
