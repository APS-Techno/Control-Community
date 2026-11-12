using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

/// <summary>Editing the script while the Visual Builder tab is hidden must not
/// shrink the block canvas to height 0: <c>Control.Visible</c> is false for
/// everything under a hidden TabPage, so MeasurePreferred cannot skip children
/// on it.</summary>
public class BlockStackControlHiddenHostTests
{
    private static DelayStatement D(int i) =>
        new(NodeField.Literal((i + 1).ToString()), null) { Id = NodeId.FromPosition("delay", "t", i) };

    [StaFact]
    public void Patch_While_HostTabHidden_Keeps_ContentHeight()
    {
        using var form = new Form { Size = new Size(600, 400) };
        var tabs = new TabControl { Dock = DockStyle.Fill, Parent = form };
        var pageBlocks = new TabPage("Blocks");
        var pageText = new TabPage("Text");
        tabs.TabPages.Add(pageBlocks);
        tabs.TabPages.Add(pageText);
        var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = pageBlocks };
        form.Show();
        Application.DoEvents();

        var body0 = ImmutableArray.Create<AstNode>(D(0));
        AstPatcher.Patch(stack, ImmutableArray<AstNode>.Empty, body0);
        Application.DoEvents();
        int visibleHeight = stack.ContentSize.Height;
        Assert.True(visibleHeight > 0, $"sanity: visible patch gave height {visibleHeight}");

        tabs.SelectedTab = pageText;
        Application.DoEvents();
        var body1 = body0.Add(D(1));
        AstPatcher.Patch(stack, body0, body1);
        Application.DoEvents();

        tabs.SelectedTab = pageBlocks;
        Application.DoEvents();

        Assert.Equal(2, stack.Blocks.Count);
        Assert.True(stack.ContentSize.Height > visibleHeight,
            $"ContentSize.Height={stack.ContentSize.Height} after patching under a hidden tab; one-block height was {visibleHeight}");
    }
}
