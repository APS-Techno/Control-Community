using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class PrettyPrinterContainerTests
{
    private static string Print(string text) => PrettyPrinter.Print(Parser.Parse(text));

    [Fact]
    public void Loop_Round_Trips()
    {
        var src = "loop 3\n  delay 1\nendloop\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void IfElse_Round_Trips()
    {
        var src = "if pm < 5\n  valve M1 open\nelse\n  valve M1 close\nendif\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void Comment_On_Its_Own_Line_Is_Preserved()
    {
        var src = "# pump down\nvalve M1 open\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void Trailing_Comment_Is_Preserved()
    {
        var src = "delay 5 # wait for pump\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void Blank_Lines_Between_Statements_Are_Preserved()
    {
        var src = "valve M1 open\n\nvalve M2 open\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void Nested_Loop_In_If_Round_Trips()
    {
        var src = "if pm < 5\n  loop 3\n    delay 1\n  endloop\nendif\n";
        Assert.Equal(src, Print(src));
    }

    [Fact]
    public void Start_Finish_Markers_Round_Trip()
    {
        var src = "start\nvalve M1 open\nfinish\n";
        Assert.Equal(src, Print(src));
    }
}
