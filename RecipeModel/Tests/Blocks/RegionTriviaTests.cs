using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RegionTriviaTests
{
    private static Trivia Comment(string text) =>
        new(ImmutableArray.Create<TriviaToken>(new LineCommentTrivia(text)));

    [Fact]
    public void PrependOpen_adds_marker_at_front()
    {
        var t = RegionTrivia.PrependOpen(Trivia.Empty, "Ar sputter", collapsed: true);
        var open = RegionTrivia.FirstOpen(t);
        Assert.Equal("Ar sputter", open!.Value.Name);
        Assert.True(open.Value.Collapsed);
    }

    [Fact]
    public void SetCollapsedOnOpen_flips_flag_preserving_name()
    {
        var t = RegionTrivia.PrependOpen(Trivia.Empty, "main", collapsed: false);
        t = RegionTrivia.SetCollapsedOnOpen(t, true);
        Assert.True(RegionTrivia.FirstOpen(t)!.Value.Collapsed);
        Assert.Equal("main", RegionTrivia.FirstOpen(t)!.Value.Name);
    }

    [Fact]
    public void RemoveRegionMarkers_keeps_user_comments()
    {
        var t = new Trivia(ImmutableArray.Create<TriviaToken>(
            new LineCommentTrivia("#region main"),
            new LineCommentTrivia("# a real comment")));
        t = RegionTrivia.RemoveRegionMarkers(t);
        Assert.Null(RegionTrivia.FirstOpen(t));
        Assert.Single(t.Tokens);
        Assert.Equal("# a real comment", ((LineCommentTrivia)t.Tokens[0]).Text);
    }

    [Fact]
    public void PrependClose_and_HasClose()
    {
        var t = RegionTrivia.PrependClose(Trivia.Empty);
        Assert.True(RegionTrivia.HasClose(t));
    }

    [Fact]
    public void InsertOpen_places_open_after_a_leading_close()
    {
        var t = RegionTrivia.PrependClose(Trivia.Empty);        // [#endregion]
        t = RegionTrivia.InsertOpen(t, "B", collapsed: false);  // expect [#endregion, #region B]

        Assert.Equal(2, t.Tokens.Length);
        Assert.True(RegionMarker.IsClose(((LineCommentTrivia)t.Tokens[0]).Text));
        Assert.Equal("B", RegionMarker.TryParseOpen(((LineCommentTrivia)t.Tokens[1]).Text)!.Value.Name);
    }

    [Fact]
    public void InsertOpen_with_no_leading_close_goes_to_front()
    {
        var t = RegionTrivia.InsertOpen(Trivia.Empty, "A", collapsed: false);
        Assert.Equal("A", RegionTrivia.FirstOpen(t)!.Value.Name);
    }
}
