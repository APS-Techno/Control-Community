using System.Drawing.Drawing2D;
using System.Linq;
using APS.Controls.Theme;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public abstract class BlockBase : UserControl
{
    public Color CategoryColor { get; }
    public string Keyword { get; }

    public bool IsSelected => ParentStack?.Selection is { } s && s.SelectedRange.Contains(this);

    /// <summary>Width of the drag gutter on the left edge (pixels).</summary>
    protected static int GutterWidth => BlockTheme.GutterWidth;

    /// <summary>True for blocks that exist only to carry trivia (e.g. a dangling
    /// trailing comment / <c>#endregion</c> preserved for round-tripping). Such blocks
    /// stay in the stack — so <see cref="RegionModel"/> can read their trivia and
    /// <see cref="ToAst"/> can reproduce them — but are never shown or laid out.</summary>
    internal virtual bool SuppressInLayout => false;

    protected readonly Panel HeaderStrip;
    protected readonly FlowLayoutPanel BodyPanel;
    protected readonly FlowLayoutPanel FieldRow;

    protected BlockBase(string keyword, Color categoryColor)
    {
        Keyword = keyword;
        CategoryColor = categoryColor;

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        SuspendLayout();
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        MinimumSize = new Size(BlockTheme.HeaderColumnWidth + BlockTheme.Scale(80), BlockTheme.HeaderHeight + 2 * BlockTheme.BlockPadding);
        Margin = new Padding(0, 0, 0, BlockTheme.BlockSpacing);
        // Reserve a frame inside the rounded shape for the border to paint into;
        // children docked inside this Padding never overlap it. Unselected, the outer
        // SelectionRingWidth is painted canvas-dark so the block keeps its 2 px category
        // frame; selected, that outer band becomes the amber ring + dark gap, so the
        // selection visibly "grows" instead of hiding under the child panels.
        Padding = new Padding(BlockTheme.Scale(2) + BlockTheme.SelectionRingWidth);
        BackColor = categoryColor;
        BorderStyle = BorderStyle.None;

        HeaderStrip = new Panel
        {
            Dock = DockStyle.Left,
            Width = BlockTheme.HeaderColumnWidth,
            BackColor = categoryColor,
        };
        var headerLabel = new Label
        {
            Text = keyword,
            Dock = DockStyle.Fill,
            AutoSize = false,
            ForeColor = BlockTheme.BlockTextOnCat,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
        };
        // The Label fills HeaderStrip, so clicks on the keyword hit the Label, not the Panel.
        // Mouse events don't bubble to the parent in WinForms, so forward them explicitly.
        headerLabel.MouseDown += OnHeaderMouseDown;
        headerLabel.MouseMove += OnHeaderMouseMove;
        headerLabel.MouseUp   += OnHeaderMouseUp;
        HeaderStrip.Controls.Add(headerLabel);

        BodyPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(BlockTheme.BlockPadding, BlockTheme.BlockPadding, BlockTheme.BlockPadding, BlockTheme.BlockPadding),
            BackColor = BlockTheme.BlockBodyBg,
        };
        FieldRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            BackColor = BlockTheme.BlockBodyBg,
        };
        BodyPanel.Controls.Add(FieldRow);

        Controls.Add(BodyPanel);
        Controls.Add(HeaderStrip);
        HeaderStrip.MouseDown += OnHeaderMouseDown;
        HeaderStrip.MouseMove += OnHeaderMouseMove;
        HeaderStrip.MouseUp   += OnHeaderMouseUp;
        BodyPanel.MouseDown   += OnBodyMouseDown;
        FieldRow.MouseDown    += OnBodyMouseDown;
        TabStop = true;
        var menu = BuildContextMenu();
        ContextMenuStrip = menu;
        // ContextMenuStrip doesn't cascade to children in WinForms — a right-click that
        // lands on the Label / HeaderStrip / BodyPanel needs the CMS set explicitly.
        HeaderStrip.ContextMenuStrip = menu;
        headerLabel.ContextMenuStrip = menu;
        BodyPanel.ContextMenuStrip   = menu;
        FieldRow.ContextMenuStrip    = menu;
        ResumeLayout();
        this.HandleCreated += (_, _) =>
        {
            HookChildFieldEvents(BodyPanel);
            DarkModeHelper.FixControls(this);
            if (ParentStack?.Selection is { } sel)
                sel.Changed += Invalidate;
        };
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        if (BodyPanel == null) return base.GetPreferredSize(proposedSize);
        int bodyW = 0, bodyH = 0;
        foreach (Control c in BodyPanel.Controls)
        {
            var p = c.GetPreferredSize(Size.Empty);
            int w = Math.Max(c.Width, p.Width) + c.Margin.Horizontal;
            int h = Math.Max(c.Height, p.Height) + c.Margin.Vertical;
            bodyW = Math.Max(bodyW, w);
            bodyH += h;
        }
        int totalW = HeaderStrip.Width + bodyW + Padding.Horizontal;
        int totalH = Math.Max(bodyH + BodyPanel.Padding.Vertical + Padding.Vertical, MinimumSize.Height);
        return new Size(Math.Max(totalW, MinimumSize.Width), totalH);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
        // A container block grows when a component is dropped into its mouth. The
        // host stack sizes its scroll flow manually and only on its OWN StackChanged,
        // so a descendant growing leaves the flow too short and it clips the taller
        // block ("half-tall" until a region fold/unfold forces a re-measure). Tell the
        // host stack to re-measure now.
        ParentStack?.OnChildBlockResized();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawRoundedBorder(e.Graphics);
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = BuildRoundedPath(0, 0, Width, Height, BlockTheme.CornerRadius);
        Region = new Region(path);
    }

    private void DrawRoundedBorder(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // The outer SelectionRingWidth band is reserved by Padding, so nothing drawn here
        // is hidden under the child panels. Selected: solid amber ring + 1 px dark gap.
        // Unselected: the band is painted canvas-dark and only the category frame shows.
        int ringBand = BlockTheme.SelectionRingWidth;              // ring + gap, in px
        if (IsSelected)
        {
            float ringW = ringBand - BlockTheme.Scale(1);
            float half = ringW / 2f;
            using var ringPath = BuildRoundedPath(half, half, Width - ringW, Height - ringW, BlockTheme.CornerRadius);
            using var ringPen = new Pen(BlockTheme.SelectionRing, ringW) { LineJoin = LineJoin.Round };
            g.DrawPath(ringPen, ringPath);

            // Thin dark separator inside the ring so it detaches from the block's own
            // header colour — otherwise amber-on-orange (POWER) reads as no ring at all.
            float sepW = BlockTheme.Scale(1);
            float sepOff = ringW + sepW / 2f;
            using var sepPath = BuildRoundedPath(sepOff, sepOff, Width - 2 * sepOff, Height - 2 * sepOff,
                Math.Max(1, BlockTheme.CornerRadius - (int)ringW));
            using var sepPen = new Pen(BlockTheme.SelectionGap, sepW) { LineJoin = LineJoin.Round };
            g.DrawPath(sepPen, sepPath);
            return;
        }
        // Unselected: paint the ring band canvas-dark so only the 2 px category frame shows.
        {
            float half = ringBand / 2f;
            using var bandPath = BuildRoundedPath(half, half, Width - ringBand, Height - ringBand, BlockTheme.CornerRadius);
            using var bandPen = new Pen(BlockTheme.SelectionGap, ringBand) { LineJoin = LineJoin.Round };
            g.DrawPath(bandPen, bandPath);
        }
        // Inset by 1 px so the antialiased pen sits fully inside the Region clip.
        var rect = new RectangleF(ringBand + 1f, ringBand + 1f, Width - 2f * (ringBand + 1), Height - 2f * (ringBand + 1));
        using var path = BuildRoundedPath(rect.X, rect.Y, rect.Width, rect.Height, Math.Max(1, BlockTheme.CornerRadius - ringBand));
        using var pen = new Pen(CategoryColor, 1.4f) { LineJoin = LineJoin.Round };
        g.DrawPath(pen, path);
    }

    private static GraphicsPath BuildRoundedPath(float x, float y, float w, float h, int radius)
    {
        float d = radius * 2f;
        var path = new GraphicsPath();
        if (d <= 0 || w < d || h < d)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }
        path.AddArc(x, y, d, d, 180, 90);
        path.AddArc(x + w - d, y, d, d, 270, 90);
        path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        path.AddArc(x, y + h - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public abstract void BindTo(AstNode node);

    /// <summary>Preserves the original node's <see cref="AstNode.Id"/>, trivia, and span
    /// if <see cref="BindTo"/> was called previously; otherwise fabricates a fresh Id.</summary>
    public abstract AstNode ToAst();

    protected AstNode? BoundNode { get; set; }

    /// <summary>Region markers live here. Never throws: an unbound block reports
    /// <see cref="Ast.Trivia.Empty"/> rather than forcing a fabrication via <see cref="ToAst"/>.</summary>
    public Ast.Trivia LeadingTrivia => BoundNode?.LeadingTrivia ?? Ast.Trivia.Empty;

    /// <summary>Same unbound-reports-empty contract as <see cref="LeadingTrivia"/>.</summary>
    public Ast.Trivia TrailingTrivia => BoundNode?.TrailingTrivia ?? Ast.Trivia.Empty;

    /// <summary>Fabricates <see cref="BoundNode"/> from current fields if unbound.
    /// Intentionally does not raise <see cref="FieldChanged"/>: trivia is not a
    /// user-editable field, so callers that need a reserialize trigger it explicitly.</summary>
    public void SetLeadingTrivia(Ast.Trivia t)
        => BoundNode = (BoundNode ?? ToAst()) with { LeadingTrivia = t };

    /// <summary>Same no-<see cref="FieldChanged"/> contract as <see cref="SetLeadingTrivia"/>.</summary>
    public void SetTrailingTrivia(Ast.Trivia t)
        => BoundNode = (BoundNode ?? ToAst()) with { TrailingTrivia = t };

    /// <summary>Caches nothing, so moves across stacks are reflected immediately.</summary>
    public BlockStackControl? ParentStack
    {
        get
        {
            for (Control? c = Parent; c != null; c = c.Parent)
                if (c is BlockStackControl s) return s;
            return null;
        }
    }

    /// <summary>May be called repeatedly, including with <c>null</c> when the host
    /// clears its catalogs.</summary>
    public virtual void ApplyContext(IBlockContext? context) { }

    public event Action? FieldChanged;

    protected void RaiseFieldChanged() => FieldChanged?.Invoke();

    /// <summary>Public trigger so container hosts can request a reserialize after
    /// editing this block's region trivia.</summary>
    public void RaisePublicFieldChanged() => RaiseFieldChanged();

    private bool _hooked;

    private Point _dragOrigin;
    private bool _dragArmed;
    private const int DragThresholdPx = 4;

    private void OnHeaderMouseDown(object? sender, MouseEventArgs e)
    {
        // Select on any button so right-click's context menu targets this block.
        // Shift-click extends the current range selection instead of collapsing to one block.
        if ((ModifierKeys & Keys.Shift) == Keys.Shift)
            ParentStack?.Selection?.SelectRangeTo(this);
        else
            ParentStack?.Selection?.Select(this);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        var ctl = (Control)sender!;
        _dragOrigin = ctl.PointToScreen(e.Location);
        _dragArmed  = true;
        ctl.Capture = true;
    }

    private void OnHeaderMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_dragArmed) return;
        var screen = ((Control)sender!).PointToScreen(e.Location);
        if (Math.Abs(screen.X - _dragOrigin.X) < DragThresholdPx &&
            Math.Abs(screen.Y - _dragOrigin.Y) < DragThresholdPx) return;

        var session = ParentStack?.DragSession;
        if (session != null && ParentStack != null)
            session.Begin(this, ParentStack);
        _dragArmed = false;
    }

    private void OnHeaderMouseUp(object? sender, MouseEventArgs e)
    {
        _dragArmed = false;
        if (sender is Control ctl) ctl.Capture = false;
        var session = ParentStack?.DragSession;
        if (session is { IsActive: true }) session.Drop();
    }

    private void OnBodyMouseDown(object? sender, MouseEventArgs e)
    {
        // Only register a select when the click did NOT land on a child field.
        // Children (combo boxes, text boxes) consume the event before bubbling.
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right) return;
        if (((Control)sender!).GetChildAtPoint(e.Location) != null) return;

        var selection = ParentStack?.Selection;
        if (selection == null) return;

        // For container blocks with an empty mouth, clicking the body targets the mouth —
        // so the next palette click inserts INTO the container rather than after it.
        if (this is IContainerBlock c)
        {
            var emptyMouth = c.Mouths.FirstOrDefault(m => m.Blocks.Count == 0);
            if (emptyMouth != null)
            {
                selection.SelectEmptyMouth(emptyMouth);
                Focus();
                return;
            }
        }
        selection.Select(this);
        Focus();
    }

    private void HookChildFieldEvents(Control root)
    {
        if (_hooked) return;
        _hooked = true;
        WalkAndHook(root);
    }

    private void WalkAndHook(Control control)
    {
        foreach (Control c in control.Controls)
        {
            bool composite = c is ParamOrLiteralEditor or CsvListField or SourceListField;
            switch (c)
            {
                case TextBox tb:               tb.TextChanged           += (_, _) => RaiseFieldChanged(); break;
                case ComboBox cb:
                    cb.SelectedIndexChanged += (_, _) => RaiseFieldChanged();
                    if (cb.DropDownStyle == ComboBoxStyle.DropDown)
                        cb.TextChanged += (_, _) => RaiseFieldChanged();
                    break;
                case CheckBox chk:             chk.CheckedChanged       += (_, _) => RaiseFieldChanged(); break;
                case ParamOrLiteralEditor pe:  pe.ValueChanged          += () => RaiseFieldChanged();     break;
                case CsvListField cl:          cl.ValueChanged          += () => RaiseFieldChanged();     break;
                case SourceListField sl:       sl.ValueChanged          += () => RaiseFieldChanged();     break;
            }
            if (!composite && c.HasChildren) WalkAndHook(c);
        }
    }

    public void InvokeDelete()
    {
        var parent = ParentStack;
        if (parent == null) return;

        int childCount = this is IContainerBlock c ? c.Mouths.Sum(m => m.Blocks.Count) : 0;
        if (childCount > 0 && !parent.DeleteConfirmer.ConfirmDeleteContainer(Keyword, childCount))
            return;

        parent.Selection?.Clear();
        parent.Remove(this);
    }

    public void InvokeDuplicate()
    {
        var parent = ParentStack;
        if (parent == null) return;

        var cloned = AstCloner.Clone(ToAst());
        var copy = BlockFactory.CreateFor(cloned);
        int at = ((IList<BlockBase>)parent.Blocks).IndexOf(this) + 1;
        parent.Insert(at, copy);
        parent.Selection?.Select(copy);
    }

    public void InvokeMoveUp()
    {
        var parent = ParentStack;
        if (parent == null) return;
        int i = ((IList<BlockBase>)parent.Blocks).IndexOf(this);
        if (i <= 0) return;
        parent.Move(this, i - 1);
    }

    public void InvokeMoveDown()
    {
        var parent = ParentStack;
        if (parent == null) return;
        int i = ((IList<BlockBase>)parent.Blocks).IndexOf(this);
        if (i < 0 || i >= parent.Blocks.Count - 1) return;
        parent.Move(this, i + 1);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!IsSelected) return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.Delete:                    InvokeDelete();    return true;
            case Keys.Control | Keys.D:          InvokeDuplicate(); return true;
            case Keys.Control | Keys.Up:         InvokeMoveUp();    return true;
            case Keys.Control | Keys.Down:       InvokeMoveDown();  return true;
            case Keys.Escape:                    ParentStack?.Selection?.Clear(); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Delete",     null, (_, _) => InvokeDelete());
        menu.Items.Add("Duplicate",  null, (_, _) => InvokeDuplicate());
        menu.Items.Add("Move up",    null, (_, _) => InvokeMoveUp());
        menu.Items.Add("Move down",  null, (_, _) => InvokeMoveDown());
        menu.Opening += (_, _) =>
        {
            int i = ParentStack is { } p ? ((IList<BlockBase>)p.Blocks).IndexOf(this) : -1;
            int n = ParentStack?.Blocks.Count ?? 0;
            menu.Items[2].Enabled = i > 0;                  // Move up
            menu.Items[3].Enabled = i >= 0 && i < n - 1;    // Move down
        };
        return menu;
    }
}
