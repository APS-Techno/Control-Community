using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class AstSyncEngineTests
{
    private static (RichTextBox text, BlockStackControl stack, AstSyncEngine engine, Form host)
        MakeEngine(string initialText)
    {
        var host = new Form();
        var text = new RichTextBox { Dock = DockStyle.Top, Text = initialText };
        var stack = new BlockStackControl { Dock = DockStyle.Fill };
        host.Controls.Add(stack);
        host.Controls.Add(text);
        host.Show();
        var engine = new AstSyncEngine(text, stack, debounceMs: 10);
        engine.SynchronizeNow();
        return (text, stack, engine, host);
    }

    [StaFact]
    public void Initial_Text_Populates_Block_Stack()
    {
        var (text, stack, engine, host) = MakeEngine("valve HV open\nmfc Ar1 5");
        try
        {
            Assert.Equal(2, stack.Blocks.Count);
            Assert.IsType<ValveBlock>(stack.Blocks[0]);
            Assert.IsType<MfcBlock>(stack.Blocks[1]);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Block_Field_Edit_Updates_Text()
    {
        var (text, stack, engine, host) = MakeEngine("valve HV open");
        try
        {
            var valve = (ValveBlock)stack.Blocks[0];
            valve.SetDeviceForTesting("M1");
            engine.SynchronizeNow();
            Assert.Contains("valve M1 open", text.Text);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Clearing_Device_Field_Does_Not_Resurrect_Action_Keyword_As_Device()
    {
        // Repro: user clears the valve's device field to type a new name. Emptying it makes
        // the block serialize to "valve  open"; reparsing that pulls "open" back in as the
        // DEVICE. The round-trip must NOT heal an empty device into the action keyword.
        var (text, stack, engine, host) = MakeEngine("valve HV open");
        try
        {
            var valve = (ValveBlock)stack.Blocks[0];
            valve.SetDeviceForTesting("");        // fires FieldChanged -> blocks->text
            engine.SynchronizeNow();              // reparse text->blocks (save / tab-switch)

            var device = (ValveStatement)valve.ToAst();
            Assert.Equal("", device.Device);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Clearing_Device_Field_Then_Debounce_Does_Not_Resurrect_Keyword()
    {
        var (text, stack, engine, host) = MakeEngine("valve HV open");
        try
        {
            var valve = (ValveBlock)stack.Blocks[0];
            valve.SetDeviceForTesting("");
            // Let the debounce fire the way it would after an interactive edit.
            for (int i = 0; i < 50; i++) { System.Threading.Thread.Sleep(10); Application.DoEvents(); }

            var device = (ValveStatement)valve.ToAst();
            Assert.Equal("", device.Device);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Text_Replace_Rebuilds_Block_Stack()
    {
        var (text, stack, engine, host) = MakeEngine("valve HV open");
        try
        {
            text.Text = "mfc Ar1 5";
            engine.SynchronizeNow();
            Assert.Single(stack.Blocks);
            Assert.IsType<MfcBlock>(stack.Blocks[0]);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Unknown_Keyword_Renders_UnknownBlock_Without_Throwing()
    {
        var (text, stack, engine, host) = MakeEngine("@@@ nonsense");
        try
        {
            engine.SynchronizeNow();
            Assert.Equal(ParseStatus.Ok, engine.Status);
            Assert.Single(stack.Blocks);
            Assert.IsType<UnknownBlock>(stack.Blocks[0]);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Trailing_Endregion_Before_Finish_Is_Not_Shown_As_Invalid_Block()
    {
        // A region closing on its own line just before `finish` leaves a trailing-trivia
        // InvalidStatement; it must render as an invisible trivia carrier, not as a
        // bogus "Invalid" block next to the loop.
        var src =
            "start\n" +
            "#region Deposition\n" +
            "loop 3\n" +
            "  holder W\n" +
            "endloop\n" +
            "#endregion\n" +
            "finish\n";
        var (text, stack, engine, host) = MakeEngine(src);
        try
        {
            engine.SynchronizeNow();
            Assert.Equal(ParseStatus.Ok, engine.Status);

            Assert.DoesNotContain(stack.Blocks, b => b is UnknownBlock);
            var carrier = Assert.Single(stack.Blocks.OfType<TriviaCarrierBlock>());
            Assert.False(carrier.Visible);
            Assert.IsType<LoopBlock>(stack.Blocks[0]);

            var region = Assert.Single(stack.Regions.Roots);
            Assert.Equal("Deposition", region.Name);

            Assert.Contains("#endregion", text.Text);
            Assert.Contains("finish", text.Text);
        }
        finally { host.Close(); host.Dispose(); }
    }
}

public class AstSyncEngineContextTests
{
    private static (RichTextBox text, BlockStackControl stack, AstSyncEngine engine, Form host)
        MakeEngine(string initialText)
    {
        var host = new Form();
        var text = new RichTextBox { Dock = DockStyle.Top, Text = initialText };
        var stack = new BlockStackControl { Dock = DockStyle.Fill };
        host.Controls.Add(stack);
        host.Controls.Add(text);
        host.Show();
        var engine = new AstSyncEngine(text, stack, debounceMs: 10);
        engine.SynchronizeNow();
        return (text, stack, engine, host);
    }

    private static BlockContext Ctx(params string[] parameters) => new()
    {
        SourceNames = ["Cr", "Ti"],
        ParameterNames = parameters,
        DevicesByCategory = new Dictionary<string, IReadOnlyList<string>>(),
    };

    [StaFact]
    public void Applying_Context_Under_RunSuppressed_Does_Not_Rewrite_Text()
    {
        // Non-canonical formatting (comment + extra spaces) must survive a context refresh.
        const string original = "# my comment\ndeposit   Cr $depTime Home\nramp PS1 $p";
        var (text, stack, engine, host) = MakeEngine(original);
        try
        {
            engine.RunSuppressed(() => stack.Context = Ctx("depTime", "p"));
            engine.RunSuppressed(() => stack.Context = Ctx("depTime", "p", "extra"));
            Assert.Equal(original, text.Text);
        }
        finally { host.Close(); host.Dispose(); }
    }

    [StaFact]
    public void Applying_Context_Keeps_Bound_Params_In_Text()
    {
        var (text, stack, engine, host) = MakeEngine("deposit Cr $depTime Home");
        try
        {
            stack.Context = Ctx("depTime");
            engine.SynchronizeNow();
            var dep = Assert.IsType<DepositBlock>(stack.Blocks[0]);
            dep.RaisePublicFieldChanged();          // any later edit re-serialises
            Assert.Contains("$depTime", text.Text);
            Assert.DoesNotContain("$ Home", text.Text);
        }
        finally { host.Close(); host.Dispose(); }
    }
}
