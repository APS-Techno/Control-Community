using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RegionModelTests
{
    private sealed class TB : BlockBase
    {
        public TB() : base("tb", System.Drawing.Color.Gray)
            => BoundNode = new LabelStatement("x") { Id = NodeId.FromPosition("l", "t", 0) };
        public override void BindTo(AstNode node) => BoundNode = node;
        public override AstNode ToAst() => BoundNode!;
    }

    private static TB Block(System.Action<TB>? setup = null) { var b = new TB(); setup?.Invoke(b); return b; }

    [StaFact]
    public void Flat_region_spans_the_run()
    {
        var b0 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "R", false)));
        var b1 = Block();
        var b2 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependClose(b.LeadingTrivia)));
        var model = RegionModel.Build(new BlockBase[] { b0, b1, b2 });

        var span = Assert.Single(model.Roots);
        Assert.Equal("R", span.Name);
        Assert.Equal(0, span.StartIndex);
        Assert.Equal(1, span.EndIndex);   // closes at b1 (marker is leading trivia of b2)
        Assert.Equal(2, span.StepCount);
        Assert.Empty(model.Warnings);
    }

    [StaFact]
    public void Region_closing_at_end_uses_trailing_trivia()
    {
        var b0 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "R", false)));
        var b1 = Block(b => b.SetTrailingTrivia(RegionTrivia.PrependClose(b.TrailingTrivia)));
        var model = RegionModel.Build(new BlockBase[] { b0, b1 });

        var span = Assert.Single(model.Roots);
        Assert.Equal(1, span.EndIndex);
    }

    [StaFact]
    public void Nested_regions_produce_children()
    {
        var b0 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "Outer", false)));
        var b1 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "Inner", true)));
        var b2 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependClose(b.LeadingTrivia)));   // closes Inner (ends b1)
        var b3 = Block(b => b.SetTrailingTrivia(RegionTrivia.PrependClose(b.TrailingTrivia))); // closes Outer (ends b3)
        var model = RegionModel.Build(new BlockBase[] { b0, b1, b2, b3 });

        var outer = Assert.Single(model.Roots);
        Assert.Equal("Outer", outer.Name);
        var inner = Assert.Single(outer.Children);
        Assert.Equal("Inner", inner.Name);
        Assert.True(inner.Collapsed);
        Assert.Equal(1, inner.StartIndex);
        Assert.Equal(1, inner.EndIndex);
    }

    [StaFact]
    public void Adjacent_regions_close_and_open()
    {
        // b0 opens region A, b1 closes A and opens B, b2 closes B.
        var b0 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "A", false)));
        var b1 = Block(b =>
        {
            // PrependOpen inserts at 0, so call it first. Then PrependClose inserts at 0 again,
            // resulting in token order [Close, Open].
            var trivia = RegionTrivia.PrependOpen(b.LeadingTrivia, "B", false);
            trivia = RegionTrivia.PrependClose(trivia);
            b.SetLeadingTrivia(trivia);
        });
        var b2 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependClose(b.LeadingTrivia)));

        var model = RegionModel.Build(new BlockBase[] { b0, b1, b2 });

        Assert.Equal(2, model.Roots.Count);

        var regionA = model.Roots[0];
        Assert.Equal("A", regionA.Name);
        Assert.Equal(0, regionA.StartIndex);
        Assert.Equal(0, regionA.EndIndex);
        Assert.Empty(regionA.Children);

        var regionB = model.Roots[1];
        Assert.Equal("B", regionB.Name);
        Assert.Equal(1, regionB.StartIndex);
        Assert.Equal(1, regionB.EndIndex);
        Assert.Empty(regionB.Children);

        Assert.Empty(model.Warnings);
    }

    [StaFact]
    public void Stray_close_and_unclosed_open_warn()
    {
        var b0 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependClose(b.LeadingTrivia)));         // stray close
        var b1 = Block(b => b.SetLeadingTrivia(RegionTrivia.PrependOpen(b.LeadingTrivia, "U", false))); // never closed
        var model = RegionModel.Build(new BlockBase[] { b0, b1 });
        Assert.Equal(2, model.Warnings.Count);
        Assert.Contains(model.Warnings, w => w.Contains("stray"));
        Assert.Contains(model.Warnings, w => w.Contains("unclosed"));
    }
}
