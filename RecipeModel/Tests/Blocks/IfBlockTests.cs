using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class IfBlockTests
{
    private static IfElseContainer MakeIf(bool withElse)
    {
        var thenChild = new ValveStatement("HV", ValveAction.Open)
        { Id = NodeId.FromPosition("valve", "if/then", 0) };
        var elseChild = new ValveStatement("HV", ValveAction.Close)
        { Id = NodeId.FromPosition("valve", "if/else", 0) };
        return new IfElseContainer
        {
            Id = NodeId.FromPosition("if", "script", 0),
            Body = ImmutableArray.Create<AstNode>(thenChild),
            ElseBody = withElse ? ImmutableArray.Create<AstNode>(elseChild) : null,
            Condition = new ConditionExpr("pm", "<", NodeField.Literal("5")),
        };
    }

    [StaFact]
    public void BindTo_Then_Branch_Populated()
    {
        var src = MakeIf(withElse: false);
        var block = new IfBlock();
        block.BindTo(src);

        Assert.Equal("pm", block.ConditionForTesting.Lhs);
        Assert.Equal("<", block.ConditionForTesting.Op);
        Assert.Equal("5", block.ConditionForTesting.Rhs.Value);
        Assert.Single(block.ThenStack.Blocks);
        Assert.Empty(block.ElseStack.Blocks);
    }

    [StaFact]
    public void BindTo_Both_Branches_Populated()
    {
        var src = MakeIf(withElse: true);
        var block = new IfBlock();
        block.BindTo(src);
        Assert.Single(block.ThenStack.Blocks);
        Assert.Single(block.ElseStack.Blocks);
    }

    [StaFact]
    public void ToAst_With_Empty_Else_Stack_Returns_Null_ElseBody()
    {
        var src = MakeIf(withElse: false);
        var block = new IfBlock();
        block.BindTo(src);

        var result = Assert.IsType<IfElseContainer>(block.ToAst());
        Assert.Null(result.ElseBody);
    }

    [StaFact]
    public void ToAst_With_NonEmpty_Else_Stack_Returns_Populated_ElseBody()
    {
        var src = MakeIf(withElse: true);
        var block = new IfBlock();
        block.BindTo(src);

        var result = Assert.IsType<IfElseContainer>(block.ToAst());
        Assert.NotNull(result.ElseBody);
        Assert.Single(result.ElseBody!.Value);
    }
}
