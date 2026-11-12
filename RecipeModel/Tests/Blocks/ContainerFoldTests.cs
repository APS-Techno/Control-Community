using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;
using System.Collections.Immutable;

namespace APS.RecipeModel.Tests.Blocks;

public class ContainerFoldTests
{
    private static LoopContainer Loop() => new()
    {
        Id = NodeId.FromPosition("loop", "t", 0),
        Body = ImmutableArray<AstNode>.Empty,
        Count = NodeField.Literal("3"),
    };

    [StaFact]
    public void SetBodyCollapsed_hides_body_and_marks_trailing_trivia()
    {
        var loop = new LoopBlock();
        loop.BindTo(Loop());

        loop.SetBodyCollapsed(true);

        Assert.True(loop.IsBodyCollapsed);
        var trailing = ((LoopContainer)loop.ToAst()).TrailingTrivia;
        Assert.Contains(trailing.Tokens, t => t is LineCommentTrivia c && c.Text.Contains("[collapsed]"));
    }

    [StaFact]
    public void Bind_restores_collapsed_from_trailing_trivia()
    {
        var node = Loop() with
        {
            TrailingTrivia = new Trivia(ImmutableArray.Create<TriviaToken>(new LineCommentTrivia("# [collapsed]")))
        };
        var loop = new LoopBlock();
        loop.BindTo(node);
        Assert.True(loop.IsBodyCollapsed);
    }

    [StaFact]
    public void SetBodyCollapsed_twice_is_idempotent_and_marks_trivia_once()
    {
        var loop = new LoopBlock();
        loop.BindTo(Loop());

        loop.SetBodyCollapsed(true);
        loop.SetBodyCollapsed(true);

        Assert.True(loop.IsBodyCollapsed);
        var trailing = ((LoopContainer)loop.ToAst()).TrailingTrivia;
        var matches = trailing.Tokens.Count(t => t is LineCommentTrivia c && c.Text.Trim() == "# [collapsed]");
        Assert.Equal(1, matches);
    }

    [StaFact]
    public void SetBodyCollapsed_collapse_then_expand_round_trips()
    {
        var loop = new LoopBlock();
        loop.BindTo(Loop());

        loop.SetBodyCollapsed(true);
        loop.SetBodyCollapsed(false);

        Assert.False(loop.IsBodyCollapsed);
        var trailing = ((LoopContainer)loop.ToAst()).TrailingTrivia;
        Assert.DoesNotContain(trailing.Tokens, t => t is LineCommentTrivia c && c.Text.Trim() == "# [collapsed]");
    }
}
