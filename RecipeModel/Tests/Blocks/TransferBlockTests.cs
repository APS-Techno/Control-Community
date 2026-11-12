using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class TransferBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new TransferStatement(NodeField.Literal("LL"), NodeField.Literal("C1"))
        { Id = NodeId.FromPosition("transfer", "script", 0) };
        var block = new TransferBlock();
        block.BindTo(src);
        var r = Assert.IsType<TransferStatement>(block.ToAst());
        Assert.Equal("LL", r.Src.Value);
        Assert.Equal("C1", r.Dst.Value);
    }

    [StaFact]
    public void Factory_Creates_TransferBlock()
    {
        var node = new TransferStatement(NodeField.Literal("LL"), NodeField.Literal("C1"))
        { Id = NodeId.FromPosition("transfer", "script", 0) };
        Assert.IsType<TransferBlock>(BlockFactory.CreateFor(node));
    }
}
