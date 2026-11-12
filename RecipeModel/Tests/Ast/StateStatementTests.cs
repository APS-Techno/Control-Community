using APS.RecipeModel.Ast;
using Xunit;

namespace APS.RecipeModel.Tests.Ast;

public class StateStatementTests
{
    private static string Print(string text) => PrettyPrinter.Print(Parser.Parse(text));

    [Theory]
    [InlineData("startup")]
    [InlineData("idle")]
    [InlineData("processing")]
    public void State_Parses_To_StateStatement(string name)
    {
        var stmt = Assert.IsType<StateStatement>(Assert.Single(Parser.Parse($"state {name}").Body));
        Assert.Equal(name, stmt.State.Value);
        Assert.False(stmt.State.IsParamRef);
    }

    [Fact]
    public void State_Is_Not_Flagged_Invalid()
    {
        Assert.IsNotType<InvalidStatement>(Assert.Single(Parser.Parse("state startup").Body));
    }

    [Theory]
    [InlineData("state startup")]
    [InlineData("state idle")]
    [InlineData("state processing")]
    [InlineData("state $target")]
    public void State_Round_Trips(string line)
    {
        Assert.Equal(line, Print(line).TrimEnd('\n'));
    }
}
