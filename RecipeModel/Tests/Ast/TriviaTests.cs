using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class TriviaTests
{
    [Fact]
    public void Empty_Trivia_Has_No_Tokens()
    {
        Assert.Empty(Trivia.Empty.Tokens);
    }

    [Fact]
    public void Trivia_With_Same_Tokens_Are_Equal()
    {
        var a = new Trivia(ImmutableArray.Create<TriviaToken>(
            new LineCommentTrivia("# hello"),
            new WhitespaceTrivia(" ")));
        var b = new Trivia(ImmutableArray.Create<TriviaToken>(
            new LineCommentTrivia("# hello"),
            new WhitespaceTrivia(" ")));
        Assert.Equal(a, b);
    }

    [Fact]
    public void SourceSpan_Records_Line_Column_Length()
    {
        var span = new SourceSpan(3, 5, 7);
        Assert.Equal(3, span.Line);
        Assert.Equal(5, span.Column);
        Assert.Equal(7, span.Length);
    }
}
