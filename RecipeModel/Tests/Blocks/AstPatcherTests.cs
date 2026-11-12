using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class AstPatcherTests
{
    private static ValveStatement V(string device, int i) =>
        new(device, ValveAction.Open) { Id = NodeId.FromPosition("valve", "t", i) };

    [StaFact]
    public void BindInPlace_Keeps_Same_UserControl_Updates_Fields()
    {
        var stack = new BlockStackControl();
        stack.Insert(0, BlockFactory.CreateFor(V("HV", 0)));
        var before = stack.Blocks[0];

        var newBody = ImmutableArray.Create<AstNode>(V("M1", 0));
        AstPatcher.Patch(stack, ImmutableArray.Create<AstNode>(V("HV", 0)), newBody);

        Assert.Same(before, stack.Blocks[0]);
        var regen = Assert.IsType<ValveStatement>(before.ToAst());
        Assert.Equal("M1", regen.Device);
    }

    [StaFact]
    public void Insert_Appends_New_Block_At_Index()
    {
        var stack = new BlockStackControl();
        stack.Insert(0, BlockFactory.CreateFor(V("HV", 0)));

        var newBody = ImmutableArray.Create<AstNode>(V("HV", 0), V("M1", 1));
        AstPatcher.Patch(stack, stack.Blocks.Select(b => b.ToAst()).ToImmutableArray(), newBody);

        Assert.Equal(2, stack.Blocks.Count);
        Assert.IsType<ValveBlock>(stack.Blocks[1]);
    }

    [StaFact]
    public void Remove_Drops_Excess_Tail_Blocks()
    {
        var stack = new BlockStackControl();
        stack.Insert(0, BlockFactory.CreateFor(V("HV", 0)));
        stack.Insert(1, BlockFactory.CreateFor(V("M1", 1)));
        var first = stack.Blocks[0];

        var newBody = ImmutableArray.Create<AstNode>(V("HV", 0));
        AstPatcher.Patch(stack, stack.Blocks.Select(b => b.ToAst()).ToImmutableArray(), newBody);

        Assert.Single(stack.Blocks);
        Assert.Same(first, stack.Blocks[0]);
    }

    [StaFact]
    public void Replace_Disposes_Old_And_Inserts_New()
    {
        var stack = new BlockStackControl();
        stack.Insert(0, BlockFactory.CreateFor(V("HV", 0)));
        var oldBlock = stack.Blocks[0];

        var newBody = ImmutableArray.Create<AstNode>(
            (AstNode)new MfcStatement("Ar1", NodeField.Literal("5"))
                { Id = NodeId.FromPosition("mfc", "t", 0) });
        AstPatcher.Patch(stack, stack.Blocks.Select(b => b.ToAst()).ToImmutableArray(), newBody);

        Assert.Single(stack.Blocks);
        Assert.IsType<MfcBlock>(stack.Blocks[0]);
        Assert.NotSame(oldBlock, stack.Blocks[0]);
    }

    [StaFact]
    public void BulkLoad_Coalesces_StackChanged_To_Avoid_On_Insert_Rebuild_Storm()
    {
        // Loading a recipe from an empty stack fans out to one InsertOp per block.
        // Each Insert must NOT trigger its own StackChanged (which drives a full
        // O(N) RebuildRegionDecorations); the whole patch should coalesce to a
        // single StackChanged so the load is O(N), not O(N^2) — the source of the
        // 5-7s freeze + flicker when opening a large recipe.
        var stack = new BlockStackControl();
        int fires = 0;
        stack.StackChanged += () => fires++;

        var body = Enumerable.Range(0, 50)
            .Select(i => (AstNode)V($"M{i}", i))
            .ToImmutableArray();

        AstPatcher.Patch(stack, ImmutableArray<AstNode>.Empty, body);

        Assert.Equal(50, stack.Blocks.Count);
        Assert.True(fires <= 1, $"StackChanged should coalesce to <=1 during bulk load, got {fires}");
    }

    [StaFact]
    public void Recurses_Into_Container_Mouths()
    {
        var stack = new BlockStackControl();

        var loopSrc = new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "t", 0),
            Body = ImmutableArray.Create<AstNode>(V("HV", 0)),
            Count = NodeField.Literal("3"),
        };
        stack.Insert(0, BlockFactory.CreateFor(loopSrc));
        var loopBlock = (LoopBlock)stack.Blocks[0];
        var innerValve = loopBlock.BodyStack.Blocks[0];

        var newInner = V("M1", 0);
        var newLoop = loopSrc with { Body = ImmutableArray.Create<AstNode>(newInner) };
        var newBody = ImmutableArray.Create<AstNode>((AstNode)newLoop);

        AstPatcher.Patch(stack,
            ImmutableArray.Create<AstNode>((AstNode)loopSrc),
            newBody);

        Assert.Same(loopBlock, stack.Blocks[0]);
        Assert.Same(innerValve, loopBlock.BodyStack.Blocks[0]);
        var regen = (ValveStatement)loopBlock.BodyStack.Blocks[0].ToAst();
        Assert.Equal("M1", regen.Device);
    }
}
