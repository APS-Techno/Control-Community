using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Tests.Ast;

public class PrettyPrinterLeafTests
{
    private static string Print(string text) => PrettyPrinter.Print(Parser.Parse(text));

    [Theory]
    [InlineData("valve M1 open")]
    [InlineData("mfc Ar1 5")]
    [InlineData("mfc Ar1 $flow")]
    [InlineData("pump P1 on")]
    [InlineData("pump SP1 on 30")]
    [InlineData("pump P1 off")]
    [InlineData("stage VS -10")]
    [InlineData("delay 5")]
    [InlineData("wait 3")]
    [InlineData("pwait pm 5 60")]
    [InlineData("swait pl 50mT 300")]
    [InlineData("bias 0")]
    [InlineData("bias $v")]
    [InlineData("bias set 1 Curr 0.5")]
    [InlineData("bias set 1 Freq BiasFreq")]
    [InlineData("bias out 1 Arb 35")]
    [InlineData("bias out 1 Arb $BiasVoltageBN")]
    [InlineData("ramp PS1 500")]
    [InlineData("set PS1 voltage 250")]
    [InlineData("label top")]
    [InlineData("goto top")]
    [InlineData("call pumpdown")]
    [InlineData("var flow 5")]
    [InlineData("rotate 30")]
    public void Single_Line_Statement_Round_Trips(string line)
    {
        Assert.Equal(line, Print(line).TrimEnd('\n'));
    }

    [Fact]
    public void Invalid_Line_Is_Printed_Verbatim()
    {
        Assert.Equal("@@@ garbage", Print("@@@ garbage").TrimEnd('\n'));
    }
}
