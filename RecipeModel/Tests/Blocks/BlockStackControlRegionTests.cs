using System.Linq;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockStackControlRegionTests
{
    private sealed class TB : BlockBase
    {
        public TB() : base("tb", System.Drawing.Color.Gray)
            => BoundNode = new LabelStatement("x") { Id = NodeId.FromPosition("l", "t", 0) };
        public override void BindTo(AstNode node) => BoundNode = node;
        public override AstNode ToAst() => BoundNode!;
    }

    [StaFact]
    public void Regions_are_recomputed_after_marker_insert()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        b0.SetLeadingTrivia(RegionTrivia.PrependOpen(b0.LeadingTrivia, "R", false));
        b1.SetTrailingTrivia(RegionTrivia.PrependClose(b1.TrailingTrivia));

        stack.RebuildRegionDecorations();

        var span = Assert.Single(stack.Regions.Roots);
        Assert.Equal("R", span.Name);
    }

    [StaFact]
    public void Collapsing_hides_the_run_and_persists_flag()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        b0.SetLeadingTrivia(RegionTrivia.PrependOpen(b0.LeadingTrivia, "R", false));
        b1.SetTrailingTrivia(RegionTrivia.PrependClose(b1.TrailingTrivia));
        stack.RebuildRegionDecorations();

        stack.SetRegionCollapsed(stack.Regions.Roots[0], true);

        Assert.Contains(b0, stack.HiddenByRegions);
        Assert.Contains(b1, stack.HiddenByRegions);
        Assert.True(RegionTrivia.FirstOpen(b0.LeadingTrivia)!.Value.Collapsed);
    }

    [StaFact]
    public void Rebuild_wires_a_header_control_into_the_layout_and_keeps_it_on_collapse()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        b0.SetLeadingTrivia(RegionTrivia.PrependOpen(b0.LeadingTrivia, "R", false));
        b1.SetTrailingTrivia(RegionTrivia.PrependClose(b1.TrailingTrivia));

        stack.RebuildRegionDecorations();

        Assert.Single(stack.RegionHeaders);

        int expandedHeight = stack.GetPreferredSize(System.Drawing.Size.Empty).Height;

        stack.SetRegionCollapsed(stack.Regions.Roots[0], true);

        // The header stays visible while collapsed so the region can be re-expanded.
        Assert.Single(stack.RegionHeaders);

        int collapsedHeight = stack.GetPreferredSize(System.Drawing.Size.Empty).Height;
        Assert.True(collapsedHeight < expandedHeight,
            $"expected collapsed height ({collapsedHeight}) < expanded height ({expandedHeight})");
    }

    [StaFact]
    public void Header_context_menu_remove_region_item_strips_the_region()
    {
        var stack = new BlockStackControl();
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2);
        RegionCommands.WrapInRegion(stack, new BlockBase[] { b0, b1 }, "R");

        var header = Assert.Single(stack.RegionHeaders);
        var menu = header.ContextMenuStrip;
        Assert.NotNull(menu);
        var item = Assert.Single(menu!.Items.Cast<ToolStripItem>());
        Assert.Equal("Remove region", item.Text);

        item.PerformClick();

        Assert.Empty(stack.Regions.Roots);
        Assert.Equal(3, stack.Blocks.Count);
    }
}
