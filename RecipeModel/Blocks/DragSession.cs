namespace APS.RecipeModel.Blocks;

public sealed class DragSession
{
    private readonly DragReorderManager _mgr = new();

    public bool IsActive => _mgr.State == DragState.DraggingExistingBlock;
    public BlockBase? DraggedBlock => _mgr.DraggedBlock;
    public BlockStackControl? HoverTarget => _mgr.HoverTarget;
    public int HoverInsertionIndex => _mgr.HoverInsertionIndex;

    public bool CanDrop =>
        IsActive &&
        _mgr.HoverTarget != null &&
        !WouldCreateCycle(_mgr.DraggedBlock!, _mgr.HoverTarget);

    public void Begin(BlockBase block, BlockStackControl sourceMouth)
        => _mgr.BeginFromBlock(block, sourceMouth);

    public void UpdateHover(BlockStackControl target, int pointerY)
        => _mgr.UpdateHover(target, pointerY);

    public void Drop()
    {
        if (!CanDrop) { _mgr.Cancel(); return; }
        var block = _mgr.DraggedBlock!;
        var src   = _mgr.SourceMouth!;
        var dst   = _mgr.HoverTarget!;
        int index = _mgr.HoverInsertionIndex;

        src.Remove(block);
        // Clamp — after Remove, dst may have fewer blocks (same mouth case).
        if (dst == src && index > dst.Blocks.Count) index = dst.Blocks.Count;
        dst.Insert(index, block);
        _mgr.Cancel();
    }

    public void Cancel() => _mgr.Cancel();

    private static bool WouldCreateCycle(BlockBase dragged, BlockStackControl target)
    {
        for (Control? c = target; c != null; c = c.Parent)
            if (c == dragged) return true;
        return false;
    }
}
