using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockSelectionTests
{
    [StaFact]
    public void Initial_State_IsEmpty()
    {
        var sel = new BlockSelection();
        Assert.Null(sel.SelectedBlock);
        Assert.Null(sel.SelectedEmptyMouth);
    }

    [StaFact]
    public void Select_FiresChanged_And_ClearsMouth()
    {
        using var block = new DelayBlock();
        using var mouth = new BlockStackControl();
        var sel = new BlockSelection();
        sel.SelectEmptyMouth(mouth);

        int fired = 0;
        sel.Changed += () => fired++;
        sel.Select(block);

        Assert.Same(block, sel.SelectedBlock);
        Assert.Null(sel.SelectedEmptyMouth);
        Assert.Equal(1, fired);
    }

    [StaFact]
    public void SelectEmptyMouth_ClearsBlock()
    {
        using var block = new DelayBlock();
        using var mouth = new BlockStackControl();
        var sel = new BlockSelection();
        sel.Select(block);
        sel.SelectEmptyMouth(mouth);

        Assert.Null(sel.SelectedBlock);
        Assert.Same(mouth, sel.SelectedEmptyMouth);
    }

    [StaFact]
    public void Clear_ResetsBoth()
    {
        using var block = new DelayBlock();
        var sel = new BlockSelection();
        sel.Select(block);
        sel.Clear();

        Assert.Null(sel.SelectedBlock);
        Assert.Null(sel.SelectedEmptyMouth);
    }

    [StaFact]
    public void ResolveInsertTarget_NoSelection_AppendsToOuter()
    {
        using var outer = new BlockStackControl();
        using var a = new DelayBlock();
        outer.Insert(0, a);
        var sel = new BlockSelection();

        var (mouth, index) = BlockSelection.ResolveInsertTarget(sel, outer);

        Assert.Same(outer, mouth);
        Assert.Equal(1, index);
    }

    [StaFact]
    public void ResolveInsertTarget_SelectedBlock_InsertsAfterInSameMouth()
    {
        using var outer = new BlockStackControl();
        using var a = new DelayBlock();
        using var b = new DelayBlock();
        outer.Insert(0, a);
        outer.Insert(1, b);
        var sel = new BlockSelection();
        sel.Select(a);

        var (mouth, index) = BlockSelection.ResolveInsertTarget(sel, outer);

        Assert.Same(outer, mouth);
        Assert.Equal(1, index);
    }

    [StaFact]
    public void ResolveInsertTarget_SelectedEmptyMouth_InsertsAtZero()
    {
        using var outer = new BlockStackControl();
        using var innerMouth = new BlockStackControl();
        var sel = new BlockSelection();
        sel.SelectEmptyMouth(innerMouth);

        var (mouth, index) = BlockSelection.ResolveInsertTarget(sel, outer);

        Assert.Same(innerMouth, mouth);
        Assert.Equal(0, index);
    }
}
