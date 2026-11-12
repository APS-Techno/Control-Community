using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class MessageBlockTests
{
    [StaFact]
    public void BindTo_Then_ToAst_RoundTrips()
    {
        var src = new MessageStatement("hello world")
        { Id = NodeId.FromPosition("message", "script", 0) };
        var block = new MessageBlock();
        block.BindTo(src);
        var r = Assert.IsType<MessageStatement>(block.ToAst());
        Assert.Equal("hello world", r.Text);
    }
}
