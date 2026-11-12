using APS.RecipeModel.Ast;
using Xunit;

namespace APS.RecipeModel.Tests.Ast;

public class RobotStatementTests
{
    private static string Print(string text) => PrettyPrinter.Print(Parser.Parse(text));

    [Fact]
    public void Transfer_Parses_To_TransferStatement_With_Faces()
    {
        var stmt = Assert.IsType<TransferStatement>(Assert.Single(Parser.Parse("transfer LL C1").Body));
        Assert.Equal("LL", stmt.Src.Value);
        Assert.Equal("C1", stmt.Dst.Value);
    }

    [Fact]
    public void Home_Parses_To_HomeStatement()
    {
        Assert.IsType<HomeStatement>(Assert.Single(Parser.Parse("home").Body));
    }

    [Fact]
    public void Abort_Parses_To_AbortStatement()
    {
        Assert.IsType<AbortStatement>(Assert.Single(Parser.Parse("abort").Body));
    }

    [Theory]
    [InlineData("transfer LL C1")]
    [InlineData("transfer $from $to")]
    [InlineData("home")]
    [InlineData("abort")]
    public void Single_Line_Statement_Round_Trips(string line)
    {
        Assert.Equal(line, Print(line).TrimEnd('\n'));
    }
}
