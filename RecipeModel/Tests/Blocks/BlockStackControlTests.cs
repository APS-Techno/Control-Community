using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockStackControlTests
{
    private sealed class StubBlock : BlockBase
    {
        public StubBlock(string keyword) : base(keyword, Color.Gray) { }
        public override void BindTo(APS.RecipeModel.Ast.AstNode node) { }
        public override APS.RecipeModel.Ast.AstNode ToAst() =>
            throw new NotImplementedException();
    }

    [StaFact]
    public void Insert_Adds_Block_At_Index()
    {
        var stack = new BlockStackControl();
        var a = new StubBlock("a");
        var b = new StubBlock("b");
        var c = new StubBlock("c");

        stack.Insert(0, a);
        stack.Insert(1, b);
        stack.Insert(1, c);

        Assert.Equal(new BlockBase[] { a, c, b }, stack.Blocks.ToArray());
    }

    [StaFact]
    public void Remove_Drops_Block_And_Shifts_Order()
    {
        var stack = new BlockStackControl();
        var a = new StubBlock("a");
        var b = new StubBlock("b");
        stack.Insert(0, a);
        stack.Insert(1, b);

        stack.Remove(a);

        Assert.Single(stack.Blocks);
        Assert.Same(b, stack.Blocks[0]);
    }

    [StaFact]
    public void Move_Rearranges_Without_Disposing()
    {
        var stack = new BlockStackControl();
        var a = new StubBlock("a");
        var b = new StubBlock("b");
        var c = new StubBlock("c");
        stack.Insert(0, a);
        stack.Insert(1, b);
        stack.Insert(2, c);

        stack.Move(a, toIndex: 2);

        Assert.Equal(new BlockBase[] { b, c, a }, stack.Blocks.ToArray());
        Assert.False(a.IsDisposed);
    }

    [StaFact]
    public void Clear_Removes_All_Blocks()
    {
        var stack = new BlockStackControl();
        stack.Insert(0, new StubBlock("a"));
        stack.Insert(1, new StubBlock("b"));
        stack.Clear();
        Assert.Empty(stack.Blocks);
    }
}
