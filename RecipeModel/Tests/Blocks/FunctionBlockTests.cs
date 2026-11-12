using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class FunctionBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var child = new DelayStatement(NodeField.Param("sec"), null)
        { Id = NodeId.FromPosition("delay", "function", 0) };
        var src = new FunctionContainer
        {
            Id = NodeId.FromPosition("function", "script", 0),
            Body = ImmutableArray.Create<AstNode>(child),
            Name = "pumpdown",
            Parameters = ImmutableArray.Create("sec"),
        };
        var block = new FunctionBlock();
        block.BindTo(src);
        var r = Assert.IsType<FunctionContainer>(block.ToAst());
        Assert.Equal("pumpdown", r.Name);
        Assert.Single(r.Parameters);
        Assert.Equal("sec", r.Parameters[0]);
        Assert.Single(r.Body);
    }
}
