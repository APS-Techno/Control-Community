using APS.RecipeModel.Ast;
using Xunit;

namespace APS.RecipeModel.Tests.Ast;

/// <summary>
/// A line may carry several statements separated by ';' (e.g.
/// "valve M1 open; valve M2 open"). The AST layer must parse each as its own
/// statement and re-emit them one per line, matching the runtime lexer which
/// already splits on ';'. The split must respect string literals and comments.
/// </summary>
public class SemicolonSplitTests
{
    private static string Print(string text) => PrettyPrinter.Print(Parser.Parse(text)).TrimEnd('\n');

    [Fact]
    public void Two_Statements_On_One_Line_Split_To_Two_Lines()
    {
        Assert.Equal("valve M1 open\nvalve M2 open", Print("valve M1 open; valve M2 open"));
    }

    [Fact]
    public void Trailing_Semicolon_Is_Dropped()
    {
        // The pretty-printer normalises the command keyword to lower-case.
        Assert.Equal("mfc 3 40", Print("MFC 3 40;"));
    }

    [Fact]
    public void Three_Chained_Bias_Statements_Split()
    {
        Assert.Equal(
            "bias set 1 Curr 0.5\nbias set 1 Freq BiasFreq\nbias set 1 Duty BiasDuty",
            Print("bias set 1 Curr 0.5; bias set 1 Freq BiasFreq; bias set 1 Duty BiasDuty"));
    }

    [Fact]
    public void Semicolon_Inside_String_Does_Not_Split()
    {
        Assert.Equal("message \"a; b\"", Print("message \"a; b\""));
    }

    [Fact]
    public void Comment_After_Semicolon_Stays_With_Last_Statement()
    {
        Assert.Equal("valve M1 open\nvalve M2 close // note", Print("valve M1 open; valve M2 close // note"));
    }

    [Fact]
    public void Split_Statements_Inside_A_Loop_Keep_Indent()
    {
        Assert.Equal("loop 2\n  valve M1 open\n  valve M2 open\nendloop",
            Print("loop 2\n  valve M1 open; valve M2 open\nendloop"));
    }
}
