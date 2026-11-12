using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class DragSessionTests
{
    [StaFact]
    public void Drop_Between_Mouths_Moves_Block()
    {
        using var src = new BlockStackControl();
        using var dst = new BlockStackControl();
        using var a   = new DelayBlock();
        src.Insert(0, a);

        var session = new DragSession();
        session.Begin(a, src);
        session.UpdateHover(dst, pointerY: 0);
        session.Drop();

        Assert.Empty(src.Blocks);
        Assert.Single(dst.Blocks);
        Assert.Same(a, dst.Blocks[0]);
    }

    [StaFact]
    public void Drop_Within_Same_Mouth_Reorders()
    {
        using var stack = new BlockStackControl();
        using var a = new DelayBlock();
        using var b = new DelayBlock();
        stack.Insert(0, a);
        stack.Insert(1, b);

        var session = new DragSession();
        session.Begin(a, stack);
        // Point past b → insertion index = 2 after source is removed, clamped to 1.
        session.UpdateHover(stack, pointerY: int.MaxValue);
        session.Drop();

        Assert.Equal(new BlockBase[] { b, a }, stack.Blocks.ToArray());
    }

    [StaFact]
    public void Cycle_Guard_Prevents_Drop_Into_Descendant()
    {
        using var outer = new BlockStackControl();
        using var loop  = new LoopBlock();
        outer.Insert(0, loop);
        var innerMouth = ((IContainerBlock)loop).Mouths[0];

        var session = new DragSession();
        session.Begin(loop, outer);
        session.UpdateHover(innerMouth, 0);

        Assert.False(session.CanDrop);

        session.Drop();
        Assert.Single(outer.Blocks);
        Assert.Same(loop, outer.Blocks[0]);
    }

    [StaFact]
    public void Cancel_Resets_State()
    {
        using var stack = new BlockStackControl();
        using var a = new DelayBlock();
        stack.Insert(0, a);

        var session = new DragSession();
        session.Begin(a, stack);
        session.Cancel();

        Assert.False(session.IsActive);
        Assert.Single(stack.Blocks);
    }
}
