using System.Collections.Immutable;
using APS.Controls.Theme;
using APS.RecipeModel.Ast;
using APS.RecipeModel.Blocks;

namespace APS.RecipeModel.Demo;

internal sealed class MainForm : Form
{
    private readonly BlockPalette _palette;
    private readonly BlockStackControl _stack;
    private readonly RichTextBox _text;
    private readonly AstSyncEngine _engine;

    public MainForm()
    {
        Text = "Block Editor Demo — live text ⇄ block sync";
        Size = new Size(1200, 800);
        BackColor = Color.FromArgb(13, 27, 42);

        _palette = new BlockPalette { Dock = DockStyle.Left };
        _palette.PaletteItemActivated += kind =>
        {
            var node = NewAstForKind(kind);
            _stack.Insert(_stack.Blocks.Count, BlockFactory.CreateFor(node));
        };

        _stack = new BlockStackControl { Dock = DockStyle.Fill };
        _text = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10f),
            BackColor = Color.FromArgb(18, 30, 48),
            ForeColor = Color.FromArgb(200, 214, 229),
            BorderStyle = BorderStyle.None,
            AcceptsTab = true,
            WordWrap = false,
        };

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 500,
            BackColor = Color.FromArgb(13, 27, 42),
        };
        split.Panel1.Controls.Add(_text);
        split.Panel2.Controls.Add(_stack);

        var seedBtn = new ToolStripButton("Load sample recipe", null, (_, _) => LoadSample());
        var clearBtn = new ToolStripButton("Clear", null, (_, _) => _text.Text = "");

        var toolbar = new ToolStrip
        {
            Dock = DockStyle.Top,
            BackColor = Color.FromArgb(22, 32, 50),
            GripStyle = ToolStripGripStyle.Hidden,
        };
        toolbar.Items.Add(seedBtn);
        toolbar.Items.Add(new ToolStripSeparator());
        toolbar.Items.Add(clearBtn);

        Controls.Add(split);
        Controls.Add(_palette);
        Controls.Add(toolbar);

        DarkModeHelper.FixControls(this);

        _engine = new AstSyncEngine(_text, _stack);
        _text.Text = "valve HV open\nmfc Ar1 5\npwait pm 5 60\n";
        _engine.SynchronizeNow();
    }

    private void LoadSample()
    {
        _text.Text = string.Join('\n', new[]
        {
            "# warm-up + deposition",
            "valve HV open",
            "mfc Ar1 5",
            "pwait pm 5 60",
            "loop 3",
            "  delay 2",
            "  valve M1 open",
            "  delay 10",
            "  valve M1 close",
            "endloop",
            "pump TP1 off",
            "",
        });
        _engine.SynchronizeNow();
    }

    private AstNode NewAstForKind(RecipeBlockKind kind)
    {
        int idx = _stack.Blocks.Count;
        var id = NodeId.FromPosition(kind.ToString(), "new", idx);
        return kind switch
        {
            RecipeBlockKind.Valve    => new ValveStatement("M1", ValveAction.Open) { Id = id },
            RecipeBlockKind.Mfc      => new MfcStatement("Ar1", NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Pump     => new PumpStatement("TP1", OnOff.On) { Id = id },
            RecipeBlockKind.Stage    => new StageStatement("VS", NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Shutter  => new ShutterStatement("S1") { Id = id },
            RecipeBlockKind.Holder   => new HolderStatement("1") { Id = id },
            RecipeBlockKind.Scan     => new ScanStatement("SC1", NodeField.Literal("0"), NodeField.Literal("100"), NodeField.Literal("1")) { Id = id },
            RecipeBlockKind.Message  => new MessageStatement("") { Id = id },
            RecipeBlockKind.Bias     => new BiasStatement(NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.BiasArb  => new BiasArbStatement("CH1",
                NodeField.Literal("0"), NodeField.Literal("0"),
                NodeField.Literal("0"), NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Power    => new PowerStatement("PS1", OnOff.On, NodeField.Literal("")) { Id = id },
            RecipeBlockKind.Ramp     => new RampStatement("PS1", NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Set      => new SetStatement("PS1", "mode", NodeField.Literal("dc")) { Id = id },
            RecipeBlockKind.Warmup   => new WarmupStatement(NodeField.Literal("0"), ImmutableArray<string>.Empty) { Id = id },
            RecipeBlockKind.Deposit  => new DepositStatement(
                NodeField.Literal("0"), NodeField.Literal("0"), NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Rotation => new RotationStatement(OnOff.On, null) { Id = id },
            RecipeBlockKind.Rotate   => new RotateStatement(NodeField.Literal("30")) { Id = id },
            RecipeBlockKind.Delay    => new DelayStatement(NodeField.Literal("1"), null) { Id = id },
            RecipeBlockKind.Wait     => new WaitStatement(NodeField.Literal("1"), null) { Id = id },
            RecipeBlockKind.Pressure => new PressureStatement("pm", NodeField.Literal("0"), NodeField.Literal("60")) { Id = id },
            RecipeBlockKind.Swait    => new SwaitStatement("pl", NodeField.Literal("0"), NodeField.Literal("60")) { Id = id },
            RecipeBlockKind.Check    => new CheckStatement("pm", CompareOp.Lt, NodeField.Literal("0")) { Id = id },
            RecipeBlockKind.Loop     => new LoopContainer
                { Id = id, Body = ImmutableArray<AstNode>.Empty, Count = NodeField.Literal("1") },
            RecipeBlockKind.IfElse   => new IfElseContainer
            {
                Id = id,
                Body = ImmutableArray<AstNode>.Empty,
                ElseBody = null,
                Condition = new ConditionExpr("", "=", NodeField.Literal("")),
            },
            RecipeBlockKind.BeginEnd => new BeginEndContainer
                { Id = id, Body = ImmutableArray<AstNode>.Empty },
            RecipeBlockKind.Label    => new LabelStatement("") { Id = id },
            RecipeBlockKind.Goto     => new GotoStatement("") { Id = id },
            RecipeBlockKind.Function => new FunctionContainer
            {
                Id = id,
                Body = ImmutableArray<AstNode>.Empty,
                Name = "",
                Parameters = ImmutableArray<string>.Empty,
            },
            RecipeBlockKind.Call     => new CallStatement("") { Id = id },
            RecipeBlockKind.Var      => new VarStatement("", NodeField.Literal("")) { Id = id },
            _ => throw new NotSupportedException(kind.ToString()),
        };
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _engine.Dispose();
        base.OnFormClosed(e);
    }
}
