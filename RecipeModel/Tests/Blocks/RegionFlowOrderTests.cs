using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

/// <summary>Blocks inserted one at a time while already carrying region markers
/// (as happens on a real recipe load) must keep their visual order. A flow
/// child-index taken from the _blocks index drifts once region headers occupy
/// flow slots.</summary>
public class RegionFlowOrderTests
{
    private sealed class TB : BlockBase
    {
        public TB() : base("tb", System.Drawing.Color.Gray)
            => BoundNode = new LabelStatement("x") { Id = NodeId.FromPosition("l", "t", 0) };
        public override void BindTo(AstNode node) => BoundNode = node;
        public override AstNode ToAst() => BoundNode!;
    }

    private static FlowLayoutPanel Flow(BlockStackControl stack) =>
        (FlowLayoutPanel)typeof(BlockStackControl)
            .GetField("_flow", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(stack)!;

    [StaFact]
    public void Blocks_keep_order_when_inserted_one_by_one_with_a_region()
    {
        var stack = new BlockStackControl();

        // Simulate a load: the first block already carries the #region open; insert
        // each block individually so RebuildRegionDecorations runs (and inserts a header)
        // between inserts.
        var b0 = new TB(); b0.SetLeadingTrivia(RegionTrivia.PrependOpen(b0.LeadingTrivia, "R", false));
        stack.Insert(0, b0);
        var b1 = new TB(); stack.Insert(1, b1);
        var b2 = new TB(); stack.Insert(2, b2);
        var b3 = new TB(); b3.SetTrailingTrivia(RegionTrivia.PrependClose(b3.TrailingTrivia));
        stack.Insert(3, b3);

        var flow = Flow(stack);

        var flowBlocks = flow.Controls.Cast<Control>().OfType<BlockBase>().ToList();
        Assert.Equal(stack.Blocks.ToList(), flowBlocks);

        Assert.Single(stack.RegionHeaders);
        int headerIdx = flow.Controls.GetChildIndex(stack.RegionHeaders[0]);
        int firstBlockIdx = flow.Controls.GetChildIndex(b0);
        Assert.True(headerIdx < firstBlockIdx,
            $"header idx {headerIdx} should precede first block idx {firstBlockIdx}");

        Assert.Equal(0, headerIdx);
    }
}
