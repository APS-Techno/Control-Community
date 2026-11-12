namespace APS.RecipeModel.Blocks;

public enum DragState
{
    Idle,
    DraggingFromPalette,
    DraggingExistingBlock,
}

/// <summary>Pure state machine; owns no mouse or DragDrop plumbing.</summary>
public sealed class DragReorderManager
{
    public DragState State { get; private set; } = DragState.Idle;
    public RecipeBlockKind? PaletteKind { get; private set; }
    public BlockBase? DraggedBlock { get; private set; }
    public BlockStackControl? SourceMouth { get; private set; }
    public BlockStackControl? HoverTarget { get; private set; }
    public int HoverInsertionIndex { get; private set; }

    public void BeginFromPalette(RecipeBlockKind kind)
    {
        State = DragState.DraggingFromPalette;
        PaletteKind = kind;
        DraggedBlock = null;
        SourceMouth = null;
    }

    public void BeginFromBlock(BlockBase block, BlockStackControl sourceMouth)
    {
        State = DragState.DraggingExistingBlock;
        DraggedBlock = block;
        SourceMouth = sourceMouth;
        PaletteKind = null;
    }

    public void UpdateHover(BlockStackControl target, int pointerY)
    {
        HoverTarget = target;
        var heights = new int[target.Blocks.Count];
        for (int i = 0; i < heights.Length; i++) heights[i] = target.Blocks[i].Height;
        HoverInsertionIndex = ComputeInsertionIndex(heights, pointerY);
    }

    public void Cancel()
    {
        State = DragState.Idle;
        PaletteKind = null;
        DraggedBlock = null;
        SourceMouth = null;
        HoverTarget = null;
        HoverInsertionIndex = 0;
    }

    /// <summary>After commit, consumer inserts/moves blocks using HoverTarget/HoverInsertionIndex,
    /// then calls Drop to reset state.</summary>
    public void Drop() => Cancel();

    public static int ComputeInsertionIndex(IReadOnlyList<int> blockHeights, int pointerY)
    {
        int y = 0;
        for (int i = 0; i < blockHeights.Count; i++)
        {
            int mid = y + blockHeights[i] / 2;
            if (pointerY < mid) return i;
            y += blockHeights[i];
        }
        return blockHeights.Count;
    }
}
