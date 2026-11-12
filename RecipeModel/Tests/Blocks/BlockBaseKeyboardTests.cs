using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockBaseKeyboardTests
{
    [StaFact]
    public void Delete_Key_RemovesLeafBlock()
    {
        using var stack = new BlockStackControl();
        using var a = new DelayBlock();
        using var b = new DelayBlock();
        stack.Insert(0, a);
        stack.Insert(1, b);
        var sel = new BlockSelection();
        stack.Selection = sel;
        stack.DeleteConfirmer = new StubDeleteConfirmer(true);
        sel.Select(a);

        a.InvokeDelete();

        Assert.DoesNotContain(a, stack.Blocks);
        Assert.Contains(b, stack.Blocks);
        Assert.Null(sel.SelectedBlock);
    }

    [StaFact]
    public void Delete_Key_NonEmptyContainer_ConsultsConfirmer_DeclinedKeepsIt()
    {
        using var outer = new BlockStackControl();
        using var loop = new LoopBlock();
        outer.Insert(0, loop);
        ((IContainerBlock)loop).Mouths[0].Insert(0, new DelayBlock());
        var sel = new BlockSelection();
        outer.Selection = sel;
        outer.DeleteConfirmer = new StubDeleteConfirmer(false);
        sel.Select(loop);

        loop.InvokeDelete();

        Assert.Contains(loop, outer.Blocks);
    }

    [StaFact]
    public void CtrlD_Duplicates_Below()
    {
        using var stack = new BlockStackControl();
        using var a = new DelayBlock();
        a.BindTo(new DelayStatement(NodeField.Literal("7"), null) { Id = NodeId.FromPosition("delay", "o", 0) });
        stack.Insert(0, a);
        var sel = new BlockSelection();
        stack.Selection = sel;
        sel.Select(a);

        a.InvokeDuplicate();

        Assert.Equal(2, stack.Blocks.Count);
        var cloneAst = (DelayStatement)stack.Blocks[1].ToAst();
        Assert.Equal(NodeField.Literal("7"), cloneAst.Seconds);
        Assert.Same(stack.Blocks[1], sel.SelectedBlock);
    }

    [StaFact]
    public void CtrlUp_MovesUp()
    {
        using var stack = new BlockStackControl();
        using var a = new DelayBlock();
        using var b = new DelayBlock();
        stack.Insert(0, a);
        stack.Insert(1, b);
        var sel = new BlockSelection();
        stack.Selection = sel;
        sel.Select(b);

        b.InvokeMoveUp();

        Assert.Same(b, stack.Blocks[0]);
        Assert.Same(a, stack.Blocks[1]);
    }
}
