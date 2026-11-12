using APS.Controls.Theme;

namespace APS.RecipeModel.Blocks;

public sealed class BlockPalette : DarkScrollPanel
{
    public event Action<RecipeBlockKind>? PaletteItemActivated;

    private static readonly Color BorderColor = Color.FromArgb(40, 60, 90);

    // Tooltip colors — match VacuumControls/docs/dark-theme-guide.md tokens.
    private static readonly Color TipBg          = Color.FromArgb(13, 27, 42);    // BgDark
    private static readonly Color TipBorder      = Color.FromArgb(40, 60, 90);    // Border
    private static readonly Color TipSyntaxFg    = Color.FromArgb(72, 169, 254);  // Accent
    private static readonly Color TipDescFg      = Color.FromArgb(119, 136, 153); // TextDim

    private static readonly int TipPadding = BlockTheme.Scale(6);
    private static readonly int TipLineGap = BlockTheme.Scale(2);

    private static readonly Font TipSyntaxFont = new("Consolas", 9f);
    private static readonly Font TipDescFont   = new("Segoe UI", 8.5f);

    private readonly ToolTip _tip;

    public BlockPalette()
    {
        _tip = new ToolTip
        {
            InitialDelay = 500,
            AutoPopDelay = 8000,
            ReshowDelay  = 200,
            OwnerDraw    = true,
        };
        _tip.Popup += OnTooltipPopup;
        _tip.Draw  += OnTooltipDraw;

        Width = BlockTheme.Scale(140);
        BackColor = Color.FromArgb(13, 27, 42); // BgDark
        ContentPanel.Padding = new Padding(BlockTheme.Scale(6));
        _nextY = ContentPanel.Padding.Top;
        BuildEntries();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(BorderColor);
        var r = ClientRectangle;
        e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
    }

    private void BuildEntries()
    {
        AddCategoryHeader("Hardware", BlockTheme.HardwareCategory);
        AddItem("valve",    RecipeBlockKind.Valve,    BlockTheme.HardwareCategory);
        AddItem("mfc",      RecipeBlockKind.Mfc,      BlockTheme.HardwareCategory);
        AddItem("pump",     RecipeBlockKind.Pump,     BlockTheme.HardwareCategory);
        AddItem("stage",    RecipeBlockKind.Stage,    BlockTheme.HardwareCategory);
        AddItem("shutter",  RecipeBlockKind.Shutter,  BlockTheme.HardwareCategory);
        AddItem("holder",   RecipeBlockKind.Holder,   BlockTheme.HardwareCategory);
        AddItem("scan",     RecipeBlockKind.Scan,     BlockTheme.HardwareCategory);
        AddItem("message",  RecipeBlockKind.Message,  BlockTheme.HardwareCategory);
        AddItem("transfer", RecipeBlockKind.Transfer, BlockTheme.HardwareCategory);
        AddItem("home",     RecipeBlockKind.Home,     BlockTheme.HardwareCategory);
        AddItem("abort",    RecipeBlockKind.Abort,    BlockTheme.HardwareCategory);

        AddCategoryHeader("Power", BlockTheme.PowerCategory);
        AddItem("bias",     RecipeBlockKind.Bias,     BlockTheme.PowerCategory);
        AddItem("biasArb",  RecipeBlockKind.BiasArb,  BlockTheme.PowerCategory);
        AddItem("power",    RecipeBlockKind.Power,    BlockTheme.PowerCategory);
        AddItem("ramp",     RecipeBlockKind.Ramp,     BlockTheme.PowerCategory);
        AddItem("set",      RecipeBlockKind.Set,      BlockTheme.PowerCategory);

        AddCategoryHeader("Process", BlockTheme.ProcessCategory);
        AddItem("warmup",   RecipeBlockKind.Warmup,   BlockTheme.ProcessCategory);
        AddItem("deposit",  RecipeBlockKind.Deposit,  BlockTheme.ProcessCategory);
        AddItem("rotation", RecipeBlockKind.Rotation, BlockTheme.ProcessCategory);
        AddItem("rotate",   RecipeBlockKind.Rotate,   BlockTheme.ProcessCategory);

        AddCategoryHeader("Timing", BlockTheme.TimingCategory);
        AddItem("delay",    RecipeBlockKind.Delay,    BlockTheme.TimingCategory);
        AddItem("wait",     RecipeBlockKind.Wait,     BlockTheme.TimingCategory);
        AddItem("pwait",    RecipeBlockKind.Pressure, BlockTheme.TimingCategory);
        AddItem("swait",    RecipeBlockKind.Swait,    BlockTheme.TimingCategory);
        AddItem("check",    RecipeBlockKind.Check,    BlockTheme.TimingCategory);

        AddCategoryHeader("Flow", BlockTheme.FlowCategory);
        AddItem("loop",     RecipeBlockKind.Loop,     BlockTheme.FlowCategory);
        AddItem("if / else", RecipeBlockKind.IfElse,  BlockTheme.FlowCategory);
        AddItem("begin",    RecipeBlockKind.BeginEnd, BlockTheme.FlowCategory);
        AddItem("label",    RecipeBlockKind.Label,    BlockTheme.FlowCategory);
        AddItem("goto",     RecipeBlockKind.Goto,     BlockTheme.FlowCategory);
        AddItem("function", RecipeBlockKind.Function, BlockTheme.FlowCategory);
        AddItem("state",    RecipeBlockKind.State,    BlockTheme.FlowCategory);

        AddCategoryHeader("Subroutines", BlockTheme.SubroutineCategory);
        AddItem("call",     RecipeBlockKind.Call,     BlockTheme.SubroutineCategory);
        AddItem("var",      RecipeBlockKind.Var,      BlockTheme.SubroutineCategory);
    }

    private int _nextY;

    private void AddCategoryHeader(string name, Color color)
    {
        int innerWidth = ContentPanel.ClientSize.Width - ContentPanel.Padding.Horizontal;
        var label = new Label
        {
            Text = name.ToUpperInvariant(),
            Location = new Point(ContentPanel.Padding.Left, _nextY),
            Size = new Size(innerWidth, BlockTheme.Scale(18)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = color,
            ForeColor = BlockTheme.BlockTextOnCat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(BlockTheme.Scale(6), 0, 0, 0),
            Font = new Font("Segoe UI", 8f, FontStyle.Bold),
        };
        Controls.Add(label);
        _nextY += label.Height + BlockTheme.Scale(1);
    }

    private void AddItem(string text, RecipeBlockKind kind, Color color)
    {
        int innerWidth = ContentPanel.ClientSize.Width - ContentPanel.Padding.Horizontal;
        var btn = new Button
        {
            Text = text,
            Location = new Point(ContentPanel.Padding.Left + BlockTheme.Scale(2), _nextY),
            Size = new Size(innerWidth - BlockTheme.Scale(4), BlockTheme.Scale(22)),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(30, 44, 66),
            ForeColor = BlockTheme.BlockTextOnCat,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(BlockTheme.Scale(4), 0, 0, 0),
            Tag = kind,
            UseVisualStyleBackColor = false,
            Font = new Font("Segoe UI", 8.5f),
        };
        btn.FlatAppearance.BorderColor = color;
        btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(40, 60, 90);
        btn.Click += (_, _) => PaletteItemActivated?.Invoke(kind);

        var info = BlockMetadata.Get(kind);
        _tip.SetToolTip(btn, $"{info.Syntax}\n{info.Description}");

        Controls.Add(btn);
        _nextY += btn.Height + BlockTheme.Scale(2);
    }

    private void OnTooltipPopup(object? sender, PopupEventArgs e)
    {
        if (sender is not ToolTip tip || e.AssociatedControl is null) return;
        string text = tip.GetToolTip(e.AssociatedControl) ?? "";
        using var g = e.AssociatedControl.CreateGraphics();
        e.ToolTipSize = MeasureTooltip(text, g);
    }

    private void OnTooltipDraw(object? sender, DrawToolTipEventArgs e)
    {
        DrawTooltip(e.Graphics, e.Bounds, e.ToolTipText ?? "");
    }

    internal static Size MeasureTooltip(string text, Graphics g)
    {
        var (syntax, desc) = SplitLines(text);
        Size synSize = TextRenderer.MeasureText(g, syntax, TipSyntaxFont, Size.Empty, TextFormatFlags.NoPadding);
        Size dscSize = desc.Length > 0
            ? TextRenderer.MeasureText(g, desc, TipDescFont, Size.Empty, TextFormatFlags.NoPadding)
            : Size.Empty;

        int width  = Math.Max(synSize.Width, dscSize.Width) + TipPadding * 2;
        int height = synSize.Height + (desc.Length > 0 ? TipLineGap + dscSize.Height : 0) + TipPadding * 2;
        return new Size(width, height);
    }

    internal static void DrawTooltip(Graphics g, Rectangle bounds, string text)
    {
        using var bg = new SolidBrush(TipBg);
        g.FillRectangle(bg, bounds);

        using var border = new Pen(TipBorder);
        g.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1);

        var (syntax, desc) = SplitLines(text);
        int x = bounds.X + TipPadding;
        int y = bounds.Y + TipPadding;

        TextRenderer.DrawText(g, syntax, TipSyntaxFont, new Point(x, y),
            TipSyntaxFg, TextFormatFlags.NoPadding);

        if (desc.Length > 0)
        {
            y += TextRenderer.MeasureText(g, syntax, TipSyntaxFont, Size.Empty, TextFormatFlags.NoPadding).Height + TipLineGap;
            TextRenderer.DrawText(g, desc, TipDescFont, new Point(x, y),
                TipDescFg, TextFormatFlags.NoPadding);
        }
    }

    private static (string syntax, string description) SplitLines(string text)
    {
        int nl = text.IndexOf('\n');
        return nl < 0
            ? (text, "")
            : (text[..nl].TrimEnd(), text[(nl + 1)..].TrimEnd());
    }
}
