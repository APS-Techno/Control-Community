using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockSelectionRangeTests
{
    private sealed class TB : BlockBase
    {
        public TB() : base("tb", System.Drawing.Color.Gray) { }
        public override void BindTo(AstNode node) { }
        public override AstNode ToAst() => new LabelStatement("x") { Id = NodeId.FromPosition("l","t",0) };
    }

    [StaFact]
    public void SelectRangeTo_selects_contiguous_siblings()
    {
        var stack = new BlockStackControl();
        var sel = new BlockSelection();
        stack.Selection = sel;
        var b0 = new TB(); var b1 = new TB(); var b2 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1); stack.Insert(2, b2);

        sel.Select(b0);
        sel.SelectRangeTo(b2);

        Assert.Equal(new BlockBase[] { b0, b1, b2 }, sel.SelectedRange.ToArray());
    }

    [StaFact]
    public void Select_resets_range_to_single()
    {
        var stack = new BlockStackControl();
        var sel = new BlockSelection();
        stack.Selection = sel;
        var b0 = new TB(); var b1 = new TB();
        stack.Insert(0, b0); stack.Insert(1, b1);
        sel.Select(b0); sel.SelectRangeTo(b1);
        sel.Select(b1);
        Assert.Single(sel.SelectedRange);
        Assert.Same(b1, sel.SelectedRange[0]);
    }
}
