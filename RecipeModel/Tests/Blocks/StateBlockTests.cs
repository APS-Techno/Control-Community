using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class StateBlockTests
{
    [StaFact]
    public void StateBlock_RoundTrips()
    {
        var src = new StateStatement(NodeField.Literal("idle"))
        {
            Id = NodeId.FromPosition("state", "script", 0),
        };
        var block = new StateBlock();
        block.BindTo(src);
        var back = Assert.IsType<StateStatement>(block.ToAst());
        Assert.Equal("idle", back.State.Value);
    }

    [StaFact]
    public void StateBlock_Preserves_ParamRef()
    {
        var src = new StateStatement(NodeField.Param("$target"))
        {
            Id = NodeId.FromPosition("state", "script", 0),
        };
        var block = new StateBlock();
        block.BindTo(src);
        var back = Assert.IsType<StateStatement>(block.ToAst());
        Assert.True(back.State.IsParamRef);
        Assert.Equal("target", back.State.Value);
    }

    [StaFact]
    public void BlockFactory_Creates_StateBlock()
    {
        var node = new StateStatement(NodeField.Literal("startup"))
        {
            Id = NodeId.FromPosition("state", "script", 0),
        };
        Assert.IsType<StateBlock>(BlockFactory.CreateFor(node));
    }
}
