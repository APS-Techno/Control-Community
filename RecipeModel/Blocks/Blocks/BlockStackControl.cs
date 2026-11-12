using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace APS.RecipeModel.Blocks;

/// <summary>
/// Order is authoritative via <see cref="Blocks"/>; <see cref="Insert"/>/<see cref="Remove"/>/<see cref="Move"/>/<see cref="Clear"/>
/// are the only legal mutations.
/// </summary>
public sealed class BlockStackControl : Panel, APS.Controls.Theme.ICustomScrollSource
{
    private readonly List<BlockBase> _blocks = new();
    private readonly FlowLayoutPanel _flow;
    private Point _scrollOffset = Point.Empty;
    private readonly List<RegionHeaderControl> _headers = new();
    private readonly HashSet<BlockBase> _hiddenByRegions = new();
    // Children this stack itself hid (trivia carriers, collapsed regions, headers under a
    // collapsed ancestor). Consulted by MeasurePreferred instead of Control.Visible, which
    // reports EFFECTIVE visibility and is false for every child while the hosting tab page
    // is hidden — measuring then collapsed the flow to height 0 and left the canvas blank.
    private readonly HashSet<Control> _hiddenInFlow = new();
    public RegionModel Regions { get; private set; } = RegionModel.Empty;
    public IReadOnlyCollection<BlockBase> HiddenByRegions => _hiddenByRegions;

    /// <summary>Test-only: the currently-rendered region header rows, in flow order.</summary>
    public IReadOnlyList<RegionHeaderControl> RegionHeaders => _headers;

    private static readonly Color BorderColor = Color.FromArgb(40, 60, 90);

    public BlockStackControl()
    {
        // No AutoScroll — we drive scrolling directly via ICustomScrollSource so
        // DarkScrollHost can position the proxy bars without fighting WinForms.
        BackColor = Color.FromArgb(13, 27, 42); // BgDark
        Padding = new Padding(BlockTheme.Scale(4));

        _flow = new FlowLayoutPanel
        {
            AutoSize = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        Controls.Add(_flow);
        Resize += (_, _) => UpdateFlowSize();
        StackChanged += UpdateFlowSize;
        StackChanged += RebuildRegionDecorations;
        UpdateFlowSize();
    }

    public IReadOnlyList<BlockBase> Blocks => new ReadOnlyCollection<BlockBase>(_blocks);

    public event EventHandler? ScrollMetricsChanged;

    public Size ContentSize => _flow?.Size ?? Size.Empty;

    public Size ViewportSize => new(
        Math.Max(0, ClientSize.Width - Padding.Horizontal),
        Math.Max(0, ClientSize.Height - Padding.Vertical));

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Point ScrollPosition
    {
        get => _scrollOffset;
        set
        {
            var clamped = ClampScroll(value);
            if (clamped == _scrollOffset) return;
            _scrollOffset = clamped;
            ApplyFlowLocation();
            ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private Point ClampScroll(Point p)
    {
        int maxX = Math.Max(0, ContentSize.Width  - ViewportSize.Width);
        int maxY = Math.Max(0, ContentSize.Height - ViewportSize.Height);
        return new Point(Math.Clamp(p.X, 0, maxX), Math.Clamp(p.Y, 0, maxY));
    }

    private void ApplyFlowLocation()
    {
        if (_flow == null) return;
        _flow.Location = new Point(Padding.Left - _scrollOffset.X, Padding.Top - _scrollOffset.Y);
    }

    private bool _inUpdateFlowSize;

    private void UpdateFlowSize()
    {
        // Guard re-entrancy: OnChildBlockResized can fire while a nested layout is in
        // flight; setting _flow.Size below must not recurse back through here.
        if (_flow == null || _inUpdateFlowSize) return;
        _inUpdateFlowSize = true;
        try
        {
            var s = MeasurePreferred(Size.Empty);
            int width = Math.Max(s.Width, ViewportSize.Width);
            _flow.Size = new Size(width, s.Height);
            // Re-clamp in case content shrank below the current scroll offset.
            _scrollOffset = ClampScroll(_scrollOffset);
            ApplyFlowLocation();
            ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally { _inUpdateFlowSize = false; }
    }

    /// <summary>A child block changed height — most often a container block growing
    /// when a component is dropped into its mouth. Re-measure so <see cref="_flow"/>
    /// isn't left too short and clipping the now-taller block. Suppressed during a
    /// <see cref="BeginBatch"/> scope: <see cref="EndBatch"/> re-measures once at the
    /// end, so per-child re-measures here would reintroduce the O(N²) bulk-load cost.</summary>
    internal void OnChildBlockResized()
    {
        if (_batchDepth > 0) return;
        UpdateFlowSize();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int notches = e.Delta / 120;
        if (notches == 0) return;
        int step = SystemInformation.MouseWheelScrollLines * BlockTheme.Scale(16);
        ScrollPosition = new Point(_scrollOffset.X, _scrollOffset.Y - notches * step);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var s = MeasurePreferred(proposedSize);
        int w = Math.Max(s.Width, proposedSize.Width);
        return new Size(w, s.Height);
    }

    private Size MeasurePreferred(Size proposed)
    {
        int w = 0, h = 0;
        foreach (Control c in _flow.Controls)
        {
            if (_hiddenInFlow.Contains(c)) continue;
            var p = c.GetPreferredSize(proposed);
            w = Math.Max(w, Math.Max(c.Width, p.Width) + c.Margin.Horizontal);
            h += Math.Max(c.Height, p.Height) + c.Margin.Vertical;
        }
        return new Size(w, h);
    }

    public event Action? StackChanged;

    private int _batchDepth;
    private bool _pendingStackChanged;

    /// <summary>Coalesce a burst of <see cref="Insert"/>/<see cref="Remove"/>/<see cref="Move"/>/<see cref="Clear"/>
    /// mutations into a single <see cref="StackChanged"/> — and thus one region rebuild + layout pass —
    /// dispatched when the outermost scope is disposed. Without it, every inserted block would fire its
    /// own O(N) rebuild, making a full-recipe load O(N²). Flow layout is also suspended for the duration
    /// so intermediate inserts don't thrash. Nestable.</summary>
    public IDisposable BeginBatch()
    {
        if (_batchDepth++ == 0)
        {
            _pendingStackChanged = false;
            _flow.SuspendLayout();
        }
        return new BatchScope(this);
    }

    private void EndBatch()
    {
        if (_batchDepth == 0) return;
        if (--_batchDepth == 0)
        {
            _flow.ResumeLayout(false);
            if (_pendingStackChanged)
            {
                _pendingStackChanged = false;
                StackChanged?.Invoke();
            }
        }
    }

    private void RaiseStackChanged()
    {
        if (_batchDepth > 0) { _pendingStackChanged = true; return; }
        StackChanged?.Invoke();
    }

    private sealed class BatchScope : IDisposable
    {
        private BlockStackControl? _owner;
        public BatchScope(BlockStackControl owner) => _owner = owner;
        public void Dispose() { _owner?.EndBatch(); _owner = null; }
    }

    private IBlockContext? _context;

    /// <summary>Editor-time catalogs propagated to every block in the stack.
    /// Setting this fans out via <see cref="BlockBase.ApplyContext"/>; later
    /// inserts re-apply the current context to the new block.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IBlockContext? Context
    {
        get => _context;
        set
        {
            _context = value;
            foreach (var b in _blocks) b.ApplyContext(value);
        }
    }

    private BlockSelection? _selection;

    /// <summary>Shared selection state. Setting this fans out to every child block's
    /// current parent stack and to every nested <see cref="BlockStackControl"/> inside
    /// <see cref="IContainerBlock"/> blocks. New blocks inserted later pick it up
    /// automatically via <see cref="Insert"/>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public BlockSelection? Selection
    {
        get => _selection;
        set
        {
            if (_selection == value) return;
            if (_selection != null) _selection.Changed -= InvalidateSelectionVisual;
            _selection = value;
            if (_selection != null) _selection.Changed += InvalidateSelectionVisual;
            foreach (var b in _blocks) PropagateSelectionInto(b);
            Invalidate();
        }
    }

    private void PropagateSelectionInto(BlockBase block)
    {
        if (block is IContainerBlock c)
            foreach (var m in c.Mouths) m.Selection = _selection;
    }

    private void InvalidateSelectionVisual() => Invalidate();

    private DragSession? _dragSession;

    /// <summary>Host-scoped drag session — shared across every nested mouth
    /// so any mouth can be a drop target regardless of where the drag started.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DragSession? DragSession
    {
        get => _dragSession;
        set
        {
            _dragSession = value;
            foreach (var b in _blocks) PropagateDragSessionInto(b);
        }
    }

    private void PropagateDragSessionInto(BlockBase block)
    {
        if (block is IContainerBlock c)
            foreach (var m in c.Mouths) m.DragSession = _dragSession;
    }

    private IDeleteConfirmer _deleteConfirmer = new StubDeleteConfirmer(true);

    /// <summary>Strategy for confirming non-empty container deletion. Default = always-yes stub;
    /// the production host overrides with <see cref="MessageBoxDeleteConfirmer"/>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IDeleteConfirmer DeleteConfirmer
    {
        get => _deleteConfirmer;
        set
        {
            _deleteConfirmer = value;
            foreach (var b in _blocks) PropagateDeleteConfirmerInto(b);
        }
    }

    private void PropagateDeleteConfirmerInto(BlockBase block)
    {
        if (block is IContainerBlock c)
            foreach (var m in c.Mouths) m.DeleteConfirmer = _deleteConfirmer;
    }

    public void Insert(int index, BlockBase block)
    {
        if (index < 0 || index > _blocks.Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        _blocks.Insert(index, block);
        _flow.Controls.Add(block);
        _flow.Controls.SetChildIndex(block, index);
        if (_context != null) block.ApplyContext(_context);
        if (_selection != null) PropagateSelectionInto(block);
        if (_dragSession != null) PropagateDragSessionInto(block);
        PropagateDeleteConfirmerInto(block);
        RaiseStackChanged();
    }

    public void Remove(BlockBase block)
    {
        if (!_blocks.Remove(block)) return;
        _flow.Controls.Remove(block);
        RaiseStackChanged();
    }

    public new void Move(BlockBase block, int toIndex)
    {
        int from = _blocks.IndexOf(block);
        if (from < 0) throw new ArgumentException("block not in stack", nameof(block));
        if (toIndex < 0 || toIndex >= _blocks.Count)
            throw new ArgumentOutOfRangeException(nameof(toIndex));
        _blocks.RemoveAt(from);
        _blocks.Insert(toIndex, block);
        _flow.Controls.SetChildIndex(block, toIndex);
        RaiseStackChanged();
    }

    public void Clear()
    {
        _flow.Controls.Clear();
        _blocks.Clear();
        RaiseStackChanged();
    }

    public void RebuildRegionDecorations()
    {
        _flow.SuspendLayout();

        // Re-establish authoritative child order. Insert() sets a block's _flow child
        // index from its _blocks index, which is only valid while _flow holds blocks
        // alone; once region headers occupy slots those indices drift, scrambling the
        // order. So rebuild _flow deterministically: blocks in _blocks order, then
        // headers spliced in at their run starts by ApplyRegion.
        // Controls.Clear() detaches the focused block (or a field inside it) from the
        // tree, which drops keyboard focus entirely: after one Ctrl+Up/Down the block
        // stayed selected but ProcessCmdKey never ran again until the mouse re-focused
        // it. Remember the deepest focused descendant and give focus back afterwards.
        var focused = ContainsFocus ? FindFocusedDescendant(_flow) : null;

        _flow.Controls.Clear();
        _headers.Clear();
        _hiddenByRegions.Clear();
        _hiddenInFlow.Clear();

        foreach (var b in _blocks)
        {
            // Trivia-carrier blocks stay in _blocks (so RegionModel reads their trivia)
            // but are never shown — keep them collapsed instead of forcing Visible=true.
            b.Visible = !b.SuppressInLayout;
            if (b.SuppressInLayout) _hiddenInFlow.Add(b);
            b.Margin = new Padding(0, 0, 0, BlockTheme.BlockSpacing);
            _flow.Controls.Add(b);
        }

        Regions = RegionModel.Build(_blocks);
        ApplyRegion(Regions.Roots, depth: 0, hiddenByAncestor: false);

        _flow.ResumeLayout();
        UpdateFlowSize();

        // Headers are recreated above, so only blocks (and their fields) can be restored.
        if (focused is { IsDisposed: false } && focused.FindForm() == FindForm() && IsDescendantOfFlow(focused))
            focused.Focus();
    }

    private static Control? FindFocusedDescendant(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c.Focused) return c;
            if (c.ContainsFocus) return FindFocusedDescendant(c) ?? c;
        }
        return null;
    }

    private bool IsDescendantOfFlow(Control c)
    {
        for (var p = c.Parent; p != null; p = p.Parent)
            if (p == _flow) return true;
        return false;
    }

    private void ApplyRegion(IReadOnlyList<RegionSpan> spans, int depth, bool hiddenByAncestor)
    {
        foreach (var span in spans)
        {
            for (int i = span.StartIndex; i <= span.EndIndex && i < _blocks.Count; i++)
                _blocks[i].Margin = new Padding((depth + 1) * BlockTheme.MouthIndent, 0, 0, BlockTheme.BlockSpacing);

            // Header row directly before the run's first block in the flow.
            var header = new RegionHeaderControl(span.Name, span.StepCount, span.Collapsed, depth,
                () => SetRegionCollapsed(span, !span.Collapsed));
            header.Renamed += newName => RenameRegion(span, newName);
            header.RemoveRequested += () => RegionCommands.RemoveRegion(this, span);
            _headers.Add(header);
            _flow.Controls.Add(header);
            int flowIndex = _flow.Controls.GetChildIndex(_blocks[span.StartIndex]);
            _flow.Controls.SetChildIndex(header, flowIndex);
            header.Visible = !hiddenByAncestor;
            if (hiddenByAncestor) _hiddenInFlow.Add(header);

            bool runHidden = hiddenByAncestor || span.Collapsed;
            for (int i = span.StartIndex; i <= span.EndIndex && i < _blocks.Count; i++)
                if (runHidden) { _blocks[i].Visible = false; _hiddenByRegions.Add(_blocks[i]); _hiddenInFlow.Add(_blocks[i]); }

            ApplyRegion(span.Children, depth + 1, runHidden);
        }
    }

    /// <summary>Persist a region's collapsed flag into its first block's leading trivia
    /// and rebuild. Raising the block's field-changed event reserializes the recipe.</summary>
    public void SetRegionCollapsed(RegionSpan span, bool collapsed)
    {
        if (span.StartIndex < 0 || span.StartIndex >= _blocks.Count) return;
        var first = _blocks[span.StartIndex];
        first.SetLeadingTrivia(RegionTrivia.SetCollapsedOnOpen(first.LeadingTrivia, collapsed));
        // Rebuild visuals first, then notify — serializers that react to FieldChanged
        // must see the stack already reflecting the new collapsed state.
        RebuildRegionDecorations();
        first.RaisePublicFieldChanged();
    }

    private void RenameRegion(RegionSpan span, string newName)
    {
        if (span.StartIndex < 0 || span.StartIndex >= _blocks.Count) return;
        var first = _blocks[span.StartIndex];
        var cleared = RegionTrivia.RemoveFirstOpen(first.LeadingTrivia, out bool wasCollapsed);
        // InsertOpen keeps a boundary block's #endregion ahead of the re-inserted open.
        first.SetLeadingTrivia(RegionTrivia.InsertOpen(cleared, newName, wasCollapsed));
        // Rebuild visuals first, then notify — serializers that react to FieldChanged
        // must see the stack already reflecting the new name.
        RebuildRegionDecorations();
        first.RaisePublicFieldChanged();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_selection == null) return;
        // Click landed on the Panel directly or on the flow panel (never on a child block —
        // those consume the event). Treat as empty-space click.
        var hit = GetChildAtPoint(e.Location);
        if (hit != null && hit != _flow) return;

        if (_blocks.Count == 0)
            _selection.SelectEmptyMouth(this);
        else
            _selection.Clear();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragSession is { IsActive: true })
        {
            _dragSession.UpdateHover(this, e.Location.Y);
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using (var borderPen = new Pen(BorderColor))
        {
            var r = ClientRectangle;
            e.Graphics.DrawRectangle(borderPen, 0, 0, r.Width - 1, r.Height - 1);
        }

        if (_blocks.Count == 0 && _selection?.SelectedEmptyMouth == this)
        {
            using var pen = new Pen(BlockTheme.ProcessCategory, 1f);
            var h = Math.Min(14, ClientSize.Height - 4);
            if (h >= 6)
            {
                var rect = new Rectangle(2, 2, Math.Max(10, ClientSize.Width - 5), h);
                e.Graphics.DrawRectangle(pen, rect);
            }
            return;
        }

        if (_dragSession is { IsActive: true } && _dragSession.HoverTarget == this && _dragSession.CanDrop)
        {
            int y = 0;
            for (int i = 0; i < _dragSession.HoverInsertionIndex && i < _blocks.Count; i++)
                y += _blocks[i].Height;
            using var dragPen = new Pen(_dragSession.DraggedBlock!.CategoryColor, 2f);
            e.Graphics.DrawLine(dragPen, 0, y, ClientSize.Width, y);
        }

        DrawRegionRails(e.Graphics, Regions.Roots, depth: 0);
    }

    private void DrawRegionRails(Graphics g, IReadOnlyList<RegionSpan> spans, int depth)
    {
        var off = _flow.Location;
        foreach (var span in spans)
        {
            if (span.StartIndex >= _blocks.Count) continue;
            var first = _blocks[span.StartIndex];
            if (first.Visible)
            {
                var last = _blocks[Math.Min(span.EndIndex, _blocks.Count - 1)];
                int x = first.Left + off.X - BlockTheme.Scale(6);
                int top = first.Top + off.Y;
                int bottom = last.Bottom + off.Y;
                using var pen = new Pen(Color.FromArgb(70, 90, 120), 1f);
                if (bottom > top && x > 0) g.DrawLine(pen, x, top, x, bottom);
                DrawRegionRails(g, span.Children, depth + 1);
            }
        }
    }

    /// <summary>Test-only: simulate a click on the empty space.</summary>
    public void SimulateEmptySpaceClick()
    {
        OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 10, 10, 0));
    }
}
