using System.Collections.Immutable;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

/// <summary>Regression tests — an empty container block must not reserve
/// vertical space for its absent body mouth. The mouth's margin and preferred
/// size both need to collapse.</summary>
public class EmptyContainerHeightTests
{
    [StaFact]
    public void Empty_LoopBlock_HeightMatches_LeafBlock()
    {
        using var form = new Form { Size = new Size(600, 400) };
        using var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };

        var loopNode = new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "t", 0),
            Body = ImmutableArray<AstNode>.Empty,
            Count = NodeField.Literal("3"),
        };
        var loop = BlockFactory.CreateFor(loopNode);

        var delayNode = new DelayStatement(NodeField.Literal("1"), null)
            { Id = NodeId.FromPosition("delay", "t", 0) };
        var delay = BlockFactory.CreateFor(delayNode);

        stack.Insert(0, loop);
        stack.Insert(1, delay);
        form.Show();
        Application.DoEvents();

        int leafH = delay.Height;
        int loopH = loop.Height;

        // 20 px tolerance: Loop has an extra field row with padding.
        Assert.True(loopH - leafH <= 20,
            $"Empty LoopBlock height {loopH} exceeds leaf height {leafH} by more than 20 px " +
            $"(delta = {loopH - leafH}). Empty mouth margin or placeholder is inflating the block.");
    }

    [StaFact]
    public void Empty_LoopBlock_HeightMatches_LeafBlock_ViaAstSyncEngine()
    {
        using var form = new Form { Size = new Size(600, 400) };
        using var text = new RichTextBox { Dock = DockStyle.Top, Height = 100, Parent = form };
        using var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };

        text.Text = "loop 3\nendloop\ndelay 1\n";
        using var engine = new AstSyncEngine(text, stack, debounceMs: 1);
        engine.SynchronizeNow();

        form.Show();
        Application.DoEvents();

        Assert.Equal(2, stack.Blocks.Count);
        var loop = stack.Blocks[0];
        var delay = stack.Blocks[1];
        int delta = loop.Height - delay.Height;
        Assert.True(delta <= 20,
            $"Via AstSyncEngine: empty Loop height {loop.Height} exceeds Delay height {delay.Height} by {delta} px.");
    }

    [StaFact]
    public void Recipe_Loop_With_Body_InsertedExactly_Once()
    {
        // AstPatcher must not re-insert container children that
        // BlockFactory.CreateFor + BindTo already populated.
        using var form = new Form { Size = new Size(600, 400) };
        using var text = new RichTextBox { Dock = DockStyle.Top, Height = 100, Parent = form };
        using var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };

        text.Text = "loop 3\ndelay 1\ndelay 2\ndelay 3\nendloop\n";
        using var engine = new AstSyncEngine(text, stack, debounceMs: 1);
        engine.SynchronizeNow();
        form.Show();
        Application.DoEvents();

        var loop = (IContainerBlock)stack.Blocks[0];
        Assert.Equal(3, loop.Mouths[0].Blocks.Count);
    }

    [StaFact]
    public void Inserting_Into_Last_Container_Mouth_Grows_Outer_Flow()
    {
        // Dropping a component into a container mouth grows the container; the outer
        // stack's manually-sized scroll flow must re-measure, or the FlowLayoutPanel
        // clips the taller container to "half-tall".
        using var form = new Form { Size = new Size(600, 400) };
        using var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };

        var leaf = BlockFactory.CreateFor(
            new DelayStatement(NodeField.Literal("1"), null) { Id = NodeId.FromPosition("delay", "t", 0) });
        var loop = BlockFactory.CreateFor(new LoopContainer
        {
            Id = NodeId.FromPosition("loop", "t", 0),
            Body = ImmutableArray<AstNode>.Empty,
            Count = NodeField.Literal("3"),
        });
        stack.Insert(0, leaf);
        stack.Insert(1, loop);   // loop is the last block ("last region")
        form.Show();
        Application.DoEvents();

        int flowBefore = stack.ContentSize.Height;

        var mouth = ((IContainerBlock)loop).Mouths[0];
        mouth.Insert(0, BlockFactory.CreateFor(
            new DelayStatement(NodeField.Literal("2"), null) { Id = NodeId.FromPosition("delay2", "t", 0) }));
        Application.DoEvents();

        int flowAfter = stack.ContentSize.Height;

        // The outer flow must have grown to contain the now-taller loop; otherwise the
        // loop's bottom is clipped by the flow bounds.
        Assert.True(flowAfter > flowBefore,
            $"Outer flow height did not grow after inserting into the container mouth " +
            $"(before={flowBefore}, after={flowAfter}); the taller container is clipped.");
        Assert.True(flowAfter >= leaf.Bottom + loop.Height,
            $"Outer flow ({flowAfter}) is shorter than leaf+loop content " +
            $"(leaf.Bottom={leaf.Bottom} + loopH={loop.Height}) — last container is clipped.");
    }

    [StaFact]
    public void SiN_BN_Recipe_Loop_Has_Ten_Children_Not_Twenty()
    {
        // Body from SiN-BN.rcpx: a double insert rendered its 10 statements as 20
        // plus a tall empty mouth.
        using var form = new Form { Size = new Size(600, 800) };
        using var text = new RichTextBox { Dock = DockStyle.Top, Height = 200, Parent = form };
        using var stack = new BlockStackControl { Dock = DockStyle.Fill, Parent = form };

        text.Text =
            "start\n" +
            "holder Si\n" +
            "deposit Si 10 Ion\n" +
            "loop 3\n" +
            "  holder Si\n" +
            "  biasArb 1 35 10 90 0\n" +
            "  bias 999\n" +
            "  deposit Si 19 Ion\n" +
            "  bias 0\n" +
            "  holder B\n" +
            "  biasArb 1 35 10 90 0\n" +
            "  bias 999\n" +
            "  deposit B 40 Ion\n" +
            "  bias 0\n" +
            "endloop\n" +
            "holder 1\n" +
            "finish\n";
        using var engine = new AstSyncEngine(text, stack, debounceMs: 1);
        engine.SynchronizeNow();
        form.Show();
        Application.DoEvents();

        var loop = stack.Blocks.OfType<LoopBlock>().Single();
        var mouth = ((IContainerBlock)loop).Mouths[0];
        Assert.Equal(10, mouth.Blocks.Count);

        int mouthH = mouth.Height;
        int loopH = loop.Height;
        Assert.True(loopH < 700,
            $"LoopBlock height is {loopH} (mouth {mouthH}) — still inflated?");

        foreach (var child in mouth.Blocks)
        {
            Assert.True(child.Visible,
                $"Child block {child.Keyword} is Visible=false — it won't render.");
        }
    }
}
