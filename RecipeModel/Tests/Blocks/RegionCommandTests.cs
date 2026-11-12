using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RegionCommandTests
{
    private sealed class TB : BlockBase
    {
        public TB() : base("tb", System.Drawing.Color.Gray)
            => BoundNode = new LabelStatement("x") { Id = NodeId.FromPosition("l","t",0) };
        public override void BindTo(AstNode node) => BoundNode = node;
        public override AstNode ToAst() => BoundNode!;
    }

    [StaFact]
    public void WrapInRegion_brackets_the_run_with_balanced_markers()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2);

        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "R");

        Assert.Equal("R", RegionTrivia.FirstOpen(b0.LeadingTrivia)!.Value.Name);
        // close attaches to leading trivia of the block after the run (b2)
        Assert.True(RegionTrivia.HasClose(b2.LeadingTrivia));
        var span = Assert.Single(stack.Regions.Roots);
        Assert.Equal(0, span.StartIndex);
        Assert.Equal(1, span.EndIndex);
    }

    [StaFact]
    public void WrapInRegion_at_end_uses_trailing_close()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "R");
        Assert.True(RegionTrivia.HasClose(b1.TrailingTrivia));
    }

    [StaFact]
    public void RemoveRegion_strips_both_markers_keeping_blocks()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2);
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "R");

        RegionCommands.RemoveRegion(stack, stack.Regions.Roots[0]);

        Assert.Empty(stack.Regions.Roots);
        Assert.Equal(3, stack.Blocks.Count);
        Assert.Null(RegionTrivia.FirstOpen(b0.LeadingTrivia));
        // The close marker (placed on b2's leading trivia by WrapInRegion) must also
        // be gone, or a broken RemoveFirstClose no-op could pass via a leftover
        // stray "#endregion" that RegionModel.Build would report as a warning.
        Assert.False(RegionTrivia.HasClose(b2.LeadingTrivia));
    }

    [StaFact]
    public void SetAllCollapsed_collapses_every_region()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "R");
        RegionCommands.SetAllCollapsed(stack, true);
        Assert.True(stack.Regions.Roots[0].Collapsed);
    }

    [StaFact]
    public void SetAllCollapsed_recurses_into_nested_regions()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB(); var b3 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2); stack.Insert(3, b3);

        // Inner region first (b1..b2), then an outer region that supersets it (b0..b3),
        // via two WrapInRegion calls — this is how nesting is built by the editor UI.
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b1, b2 }, "Inner");
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1, b2, b3 }, "Outer");

        RegionCommands.SetAllCollapsed(stack, true);

        var outer = stack.Regions.Roots[0];
        var inner = Assert.Single(outer.Children);
        Assert.Equal("Outer", outer.Name);
        Assert.Equal("Inner", inner.Name);
        // A non-recursive SetAllCollapsed would collapse only the root and leave
        // the nested child expanded — assert both to catch that regression.
        Assert.True(outer.Collapsed);
        Assert.True(inner.Collapsed);
    }

    [StaFact]
    public void WrapInRegion_targets_the_run_parent_stack_when_it_is_a_nested_mouth()
    {
        // A run selected inside a container's mouth lives in a different
        // BlockStackControl than the top-level stack, so callers must pass
        // run[0].ParentStack (the mouth), not the outer stack.
        var outer = new BlockStackControl();
        var loop = new LoopBlock();
        outer.Insert(0, loop);

        var mouth = loop.BodyStack;
        var b0 = new TB(); var b1 = new TB();
        mouth.Insert(0, b0); mouth.Insert(1, b1);

        Assert.Same(mouth, b0.ParentStack);
        Assert.NotSame(outer, b0.ParentStack);

        RegionCommands.WrapInRegion(mouth, new BlockBase[] { b0, b1 }, "R");

        var span = Assert.Single(mouth.Regions.Roots);
        Assert.Equal("R", span.Name);
        Assert.Empty(outer.Regions.Roots);
    }

    [StaFact]
    public void WrapInRegion_adjacent_runs_are_siblings_not_nested()
    {
        // In the boundary block the second region's #region must follow the first's
        // #endregion; otherwise the second region opens-then-closes (0 steps) and
        // nests under the first.
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB(); var b3 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2); stack.Insert(3, b3);

        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "A");
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b2, b3 }, "B");

        Assert.Equal(2, stack.Regions.Roots.Count);
        var a = stack.Regions.Roots[0];
        var b = stack.Regions.Roots[1];
        Assert.Equal("A", a.Name);
        Assert.Equal(0, a.StartIndex);
        Assert.Equal(1, a.EndIndex);
        Assert.Equal(2, a.StepCount);
        Assert.Empty(a.Children);
        Assert.Equal("B", b.Name);
        Assert.Equal(2, b.StartIndex);
        Assert.Equal(3, b.EndIndex);
        Assert.Equal(2, b.StepCount);
        Assert.Empty(b.Children);
    }
}
