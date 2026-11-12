using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class BlockStackControlSelectionTests
{
    [StaFact]
    public void Selection_Set_FansOutToNestedMouths()
    {
        using var outer = new BlockStackControl();
        using var loopBlock = new LoopBlock();
        outer.Insert(0, loopBlock);

        var sel = new BlockSelection();
        outer.Selection = sel;

        Assert.Same(sel, ((IContainerBlock)loopBlock).Mouths[0].Selection);
    }

    [StaFact]
    public void Selection_AppliedToLateInserts()
    {
        using var outer = new BlockStackControl();
        var sel = new BlockSelection();
        outer.Selection = sel;

        using var loopBlock = new LoopBlock();
        outer.Insert(0, loopBlock);

        Assert.Same(sel, ((IContainerBlock)loopBlock).Mouths[0].Selection);
    }

    [StaFact]
    public void EmptyStack_ClickOnEmptySpace_SelectsMouthItself()
    {
        using var form = new Form();
        using var outer = new BlockStackControl { Parent = form, Size = new Size(200, 200) };
        var sel = new BlockSelection();
        outer.Selection = sel;
        form.Show();

        outer.SimulateEmptySpaceClick();

        Assert.Same(outer, sel.SelectedEmptyMouth);
    }
}
