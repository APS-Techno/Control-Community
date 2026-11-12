namespace APS.RecipeModel.Blocks;

/// <summary>Shared across every <see cref="BlockStackControl"/> in one editor. At most one of
/// <see cref="SelectedBlock"/> / <see cref="SelectedEmptyMouth"/> is non-null at a time.</summary>
public sealed class BlockSelection
{
    public BlockBase? SelectedBlock { get; private set; }
    public BlockStackControl? SelectedEmptyMouth { get; private set; }
    public BlockBase? RangeAnchor { get; private set; }
    private readonly List<BlockBase> _range = new();
    public IReadOnlyList<BlockBase> SelectedRange => _range;
    public event Action? Changed;

    public void Select(BlockBase block)
    {
        // Note: also re-fire (and reset the range) if a multi-block range is currently
        // selected, even though SelectedBlock already equals block (it's the range extent).
        if (SelectedBlock == block && SelectedEmptyMouth == null && RangeAnchor == null) return;
        SelectedBlock = block;
        SelectedEmptyMouth = null;
        RangeAnchor = null;
        _range.Clear();
        _range.Add(block);
        Changed?.Invoke();
    }

    public void SelectEmptyMouth(BlockStackControl mouth)
    {
        if (SelectedEmptyMouth == mouth && SelectedBlock == null) return;
        SelectedEmptyMouth = mouth;
        SelectedBlock = null;
        RangeAnchor = null;
        _range.Clear();
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (SelectedBlock == null && SelectedEmptyMouth == null) return;
        SelectedBlock = null;
        SelectedEmptyMouth = null;
        RangeAnchor = null;
        _range.Clear();
        Changed?.Invoke();
    }

    public void SelectRangeTo(BlockBase extent)
    {
        var anchor = RangeAnchor ?? SelectedBlock;
        if (anchor == null || extent.ParentStack == null || anchor.ParentStack != extent.ParentStack) return;
        var blocks = (IList<BlockBase>)extent.ParentStack.Blocks;
        int a = blocks.IndexOf(anchor), b = blocks.IndexOf(extent);
        if (a < 0 || b < 0) return;
        RangeAnchor = anchor;
        SelectedBlock = extent;
        SelectedEmptyMouth = null;
        _range.Clear();
        for (int i = Math.Min(a, b); i <= Math.Max(a, b); i++) _range.Add(blocks[i]);
        Changed?.Invoke();
    }

    public static (BlockStackControl mouth, int index) ResolveInsertTarget(
        BlockSelection selection, BlockStackControl outerStack)
    {
        if (selection.SelectedBlock is { } block && block.ParentStack is { } parent)
        {
            int idx = ((IList<BlockBase>)parent.Blocks).IndexOf(block);
            return (parent, idx + 1);
        }
        if (selection.SelectedEmptyMouth is { } mouth)
            return (mouth, 0);
        return (outerStack, outerStack.Blocks.Count);
    }
}
