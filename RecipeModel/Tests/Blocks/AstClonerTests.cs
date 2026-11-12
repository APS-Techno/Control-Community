using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class AstClonerTests
{
    [Fact]
    public void Clone_Leaf_GeneratesFreshId_PreservesFields()
    {
        var original = new DelayStatement(NodeField.Literal("5"), "wait")
            { Id = NodeId.FromPosition("delay", "orig", 0) };

        var clone = (DelayStatement)AstCloner.Clone(original);

        Assert.NotEqual(original.Id, clone.Id);
        Assert.Equal(original.Seconds, clone.Seconds);
        Assert.Equal(original.Caption, clone.Caption);
    }

    [Fact]
    public void Clone_Container_RecursesIntoBody()
    {
        var inner = new DelayStatement(NodeField.Literal("1"), null)
            { Id = NodeId.FromPosition("delay", "i", 0) };
        var loop = new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "o", 0),
            Body = ImmutableArray.Create<AstNode>(inner),
            Count = NodeField.Literal("3"),
        };

        var clone = (LoopContainer)AstCloner.Clone(loop);

        Assert.NotEqual(loop.Id, clone.Id);
        Assert.Single(clone.Body);
        Assert.NotEqual(inner.Id, clone.Body[0].Id);
        Assert.IsType<DelayStatement>(clone.Body[0]);
    }

    [Fact]
    public void Clone_IfElse_RecursesIntoBothArms()
    {
        var thenNode = new DelayStatement(NodeField.Literal("1"), null)
            { Id = NodeId.FromPosition("delay", "t", 0) };
        var elseNode = new DelayStatement(NodeField.Literal("2"), null)
            { Id = NodeId.FromPosition("delay", "e", 0) };
        var ife = new IfElseContainer
        {
            Id = NodeId.FromPosition("if", "o", 0),
            Body = ImmutableArray.Create<AstNode>(thenNode),
            ElseBody = ImmutableArray.Create<AstNode>(elseNode),
            Condition = new ConditionExpr("x", "=", NodeField.Literal("1")),
        };

        var clone = (IfElseContainer)AstCloner.Clone(ife);

        Assert.NotNull(clone.ElseBody);
        Assert.NotEqual(thenNode.Id, clone.Body[0].Id);
        Assert.NotEqual(elseNode.Id, clone.ElseBody!.Value[0].Id);
    }

    [Fact]
    public void Clone_IfElse_NullElse_StaysNull()
    {
        var ife = new IfElseContainer
        {
            Id = NodeId.FromPosition("if", "o", 0),
            Body = ImmutableArray<AstNode>.Empty,
            ElseBody = null,
            Condition = new ConditionExpr("x", "=", NodeField.Literal("1")),
        };

        var clone = (IfElseContainer)AstCloner.Clone(ife);

        Assert.Null(clone.ElseBody);
    }
}
