using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BeginEndBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips_With_Body()
    {
        var child = new ValveStatement("HV", ValveAction.Open)
        { Id = NodeId.FromPosition("valve", "begin", 0) };
        var src = new BeginEndContainer
        {
            Id = NodeId.FromPosition("begin", "script", 0),
            Body = ImmutableArray.Create<AstNode>(child),
        };
        var block = new BeginEndBlock();
        block.BindTo(src);
        var r = Assert.IsType<BeginEndContainer>(block.ToAst());
        Assert.Single(r.Body);
    }
}
