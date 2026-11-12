using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Recipe;

namespace RecipeTests;

/// <summary>
/// The DSL convention (see
/// Interpreter.ResolveVariables) is that bare identifiers auto-resolve to
/// .rcpx parameters used without the $ prefix — production recipes write
/// "loop N" (SiN-TiN-BN-B-target.rcpx, DEMO-Showcase.rcpx), so the loop
/// count must support that form alongside literals and $variables.
/// </summary>
public class LoopCountTests
{
    private static List<List<Token>> Tokenize(string text) => new Lexer().Tokenize(text);

    [Fact]
    public void GetLoopCount_LiteralNumber_Returns()
    {
        var interpreter = new Interpreter();
        var lines = Tokenize("loop 3");

        Assert.Equal(3, interpreter.GetLoopCount(lines[0]));
    }

    [Fact]
    public void GetLoopCount_DollarVariable_Resolves()
    {
        var interpreter = new Interpreter();
        interpreter.SetVariable("N", 4);
        var lines = Tokenize("loop $N");

        Assert.Equal(4, interpreter.GetLoopCount(lines[0]));
    }

    [Fact]
    public void GetLoopCount_BareIdentifier_ResolvesLikeRcpxParameter()
    {
        var interpreter = new Interpreter();
        interpreter.SetVariable("N", 2); // .rcpx parameter, used as "loop N"
        var lines = Tokenize("loop N");

        Assert.Equal(2, interpreter.GetLoopCount(lines[0]));
    }

    [Fact]
    public void GetLoopCount_UndefinedBareIdentifier_ThrowsWithName()
    {
        var interpreter = new Interpreter();
        var lines = Tokenize("loop Bogus");

        var ex = Assert.Throws<InvalidOperationException>(() => interpreter.GetLoopCount(lines[0]));
        Assert.Contains("Bogus", ex.Message);
    }
}
