using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class LoopBlockTests
{
    [StaFact]
    public void BindTo_Populates_Count_And_Body_Children()
    {
        var child = new ValveStatement("HV", ValveAction.Open)
        {
            Id = NodeId.FromPosition("valve", "loop/0", 0),
        };
        var loop = new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "script", 0),
            Body = ImmutableArray.Create<AstNode>(child),
            Count = NodeField.Literal("10"),
        };
        var block = new LoopBlock();

        block.BindTo(loop);

        Assert.Equal("10", block.CountForTesting.Value);
        Assert.False(block.CountForTesting.IsParamRef);
        Assert.Single(block.BodyStack.Blocks);
        Assert.IsType<ValveBlock>(block.BodyStack.Blocks[0]);
    }

    [StaFact]
    public void ToAst_Reflects_Count_Edit_And_Body_Shape()
    {
        var loop = new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "script", 0),
            Body = ImmutableArray<AstNode>.Empty,
            Count = NodeField.Literal("5"),
        };
        var block = new LoopBlock();
        block.BindTo(loop);
        block.SetCountForTesting(NodeField.Literal("20"));

        var result = Assert.IsType<LoopContainer>(block.ToAst());
        Assert.Equal("20", result.Count.Value);
        Assert.Empty(result.Body);
        Assert.Equal(loop.Id, result.Id);
    }
}
