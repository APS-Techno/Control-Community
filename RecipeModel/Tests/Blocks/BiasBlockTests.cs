using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BiasBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_SingleValue()
    {
        var src = new BiasStatement(NodeField.Literal("0"))
        { Id = NodeId.FromPosition("bias", "script", 0) };
        var block = new BiasBlock();
        block.BindTo(src);
        var r = Assert.IsType<BiasStatement>(block.ToAst());
        Assert.Equal(new[] { "0" }, r.Args.Select(a => a.ToDslToken()));
    }

    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_CompoundSet()
    {
        var src = new BiasStatement(ImmutableArray.Create(
            NodeField.Literal("set"), NodeField.Literal("1"),
            NodeField.Literal("Curr"), NodeField.Literal("0.5")))
        { Id = NodeId.FromPosition("bias", "script", 0) };
        var block = new BiasBlock();
        block.BindTo(src);
        var r = Assert.IsType<BiasStatement>(block.ToAst());
        Assert.Equal(new[] { "set", "1", "Curr", "0.5" }, r.Args.Select(a => a.ToDslToken()));
    }

    [StaFact]
    public void BindTo_Then_ToAst_PreservesParamRefs()
    {
        var src = new BiasStatement(ImmutableArray.Create(
            NodeField.Literal("out"), NodeField.Literal("1"),
            NodeField.Literal("Arb"), NodeField.Param("BiasVoltageBN")))
        { Id = NodeId.FromPosition("bias", "script", 0) };
        var block = new BiasBlock();
        block.BindTo(src);
        var r = Assert.IsType<BiasStatement>(block.ToAst());
        Assert.Equal(new[] { "out", "1", "Arb", "$BiasVoltageBN" }, r.Args.Select(a => a.ToDslToken()));
    }
}
