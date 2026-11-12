using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

/// <summary>Move up/down (and every other mutation) rebuilds the flow via
/// Controls.Clear(), which can drop keyboard focus — the block stays selected but
/// Ctrl+Up/Down stops working until the mouse re-focuses it.</summary>
[Collection(nameof(WindowFocusCollection))]
public class BlockStackControlFocusTests
{
    [StaFact]
    public void Move_Keeps_KeyboardFocus_On_MovedBlock()
    {
        using var form = new Form { Size = new Size(600, 400) };
        var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };
        var a = new DelayBlock(); var b = new DelayBlock(); var c = new DelayBlock();
        stack.Insert(0, a); stack.Insert(1, b); stack.Insert(2, c);
        var sel = new BlockSelection(); stack.Selection = sel;
        form.Show(); Application.DoEvents();
        sel.Select(a); a.Focus(); Application.DoEvents();
        Assert.True(a.ContainsFocus, "sanity: block focused before move");

        a.InvokeMoveDown(); Application.DoEvents();
        Assert.Equal(1, ((IList<BlockBase>)stack.Blocks).IndexOf(a));
        Assert.True(a.ContainsFocus, "focus lost after first move");

        a.InvokeMoveDown(); Application.DoEvents();
        Assert.Equal(2, ((IList<BlockBase>)stack.Blocks).IndexOf(a));
        Assert.True(a.ContainsFocus, "focus lost after second move");

        a.InvokeMoveUp(); Application.DoEvents();
        Assert.Equal(1, ((IList<BlockBase>)stack.Blocks).IndexOf(a));
        Assert.True(a.ContainsFocus, "focus lost after move up");
    }
}

[CollectionDefinition(nameof(WindowFocusCollection), DisableParallelization = true)]
public sealed class WindowFocusCollection;
