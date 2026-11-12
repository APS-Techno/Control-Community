using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class DragReorderManagerTests
{
    [StaFact]
    public void Initial_State_Is_Idle()
    {
        var mgr = new DragReorderManager();
        Assert.Equal(DragState.Idle, mgr.State);
    }

    [StaFact]
    public void BeginFromPalette_Transitions_To_DraggingFromPalette()
    {
        var mgr = new DragReorderManager();
        mgr.BeginFromPalette(RecipeBlockKind.Valve);
        Assert.Equal(DragState.DraggingFromPalette, mgr.State);
        Assert.Equal(RecipeBlockKind.Valve, mgr.PaletteKind);
    }

    [StaFact]
    public void Cancel_Returns_To_Idle()
    {
        var mgr = new DragReorderManager();
        mgr.BeginFromPalette(RecipeBlockKind.Loop);
        mgr.Cancel();
        Assert.Equal(DragState.Idle, mgr.State);
    }

    [StaFact]
    public void InsertionIndex_Halves_Block_For_Hit_Testing()
    {
        // Six mock blocks, each 50px tall, contiguous from y=0.
        var heights = new[] { 50, 50, 50, 50, 50, 50 };

        Assert.Equal(0, DragReorderManager.ComputeInsertionIndex(heights, pointerY: 10));  // top half of first block
        Assert.Equal(1, DragReorderManager.ComputeInsertionIndex(heights, pointerY: 40));  // bottom half of first block
        Assert.Equal(1, DragReorderManager.ComputeInsertionIndex(heights, pointerY: 55));  // top half of second block
        Assert.Equal(2, DragReorderManager.ComputeInsertionIndex(heights, pointerY: 80));  // bottom half of second block
        Assert.Equal(6, DragReorderManager.ComputeInsertionIndex(heights, pointerY: 500)); // past end
    }

    [StaFact]
    public void InsertionIndex_Empty_Stack_Returns_Zero()
    {
        Assert.Equal(0, DragReorderManager.ComputeInsertionIndex(Array.Empty<int>(), pointerY: 0));
        Assert.Equal(0, DragReorderManager.ComputeInsertionIndex(Array.Empty<int>(), pointerY: 500));
    }

    [StaFact]
    public void BeginFromBlock_Captures_SourceMouth()
    {
        using var stack = new BlockStackControl();
        using var block = new DelayBlock();
        stack.Insert(0, block);

        var mgr = new DragReorderManager();
        mgr.BeginFromBlock(block, stack);

        Assert.Equal(DragState.DraggingExistingBlock, mgr.State);
        Assert.Same(block, mgr.DraggedBlock);
        Assert.Same(stack, mgr.SourceMouth);
    }
}
