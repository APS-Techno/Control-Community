namespace APS.RecipeModel.Blocks;

/// <summary>Hosts nested child stacks ("mouths"). Subclasses call <see cref="AddMouth"/>
/// in their constructor for each mouth they need.</summary>
public abstract class ContainerBlockBase : BlockBase
{
    private readonly List<BlockStackControl> _mouths = new();
    private Label? _foldGlyph;

    protected IReadOnlyList<BlockStackControl> Mouths => _mouths;

    protected ContainerBlockBase(string keyword, Color categoryColor)
        : base(keyword, categoryColor)
    {
        _foldGlyph = new Label
        {
            Text = "▾",
            Dock = DockStyle.Top,
            Height = BlockTheme.HeaderHeight,
            ForeColor = BlockTheme.BlockTextOnCat,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
        };
        _foldGlyph.Click += (_, _) => SetBodyCollapsed(!IsBodyCollapsed);
        HeaderStrip.Controls.Add(_foldGlyph);
    }

    public bool IsBodyCollapsed { get; private set; }

    private const string CollapsedComment = "# [collapsed]";

    public void SetBodyCollapsed(bool collapsed)
    {
        IsBodyCollapsed = collapsed;
        BodyPanel.Visible = !collapsed;

        var trailing = TrailingTrivia;
        bool present = trailing.Tokens.Any(t => t is Ast.LineCommentTrivia c && c.Text.Trim() == CollapsedComment);
        if (collapsed && !present)
            SetTrailingTrivia(new Ast.Trivia(
                trailing.Tokens.Add(new Ast.LineCommentTrivia(CollapsedComment))));
        else if (!collapsed && present)
            SetTrailingTrivia(new Ast.Trivia(
                trailing.Tokens.RemoveAll(t => t is Ast.LineCommentTrivia c && c.Text.Trim() == CollapsedComment)));

        SyncGlyph();
        RaisePublicFieldChanged();
    }

    /// <summary>Subclasses call this at the end of BindTo to restore fold state.</summary>
    protected void RestoreCollapsedFromTrivia()
    {
        bool collapsed = TrailingTrivia.Tokens.Any(t =>
            t is Ast.LineCommentTrivia c && c.Text.Trim() == CollapsedComment);
        SetBodyCollapsedSilent(collapsed);
    }

    private void SetBodyCollapsedSilent(bool collapsed)
    {
        IsBodyCollapsed = collapsed;
        BodyPanel.Visible = !collapsed;
        SyncGlyph();
    }

    private void SyncGlyph()
    {
        if (_foldGlyph is null)
            return;
        _foldGlyph.Text = IsBodyCollapsed ? "▸" : "▾";
    }

    /// <summary>Caller is responsible for inserting a label row (e.g. "else") before calling this
    /// a second time, so operators can visually distinguish the mouths.</summary>
    protected BlockStackControl AddMouth()
    {
        var mouth = new BlockStackControl
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = false,   // only the top-level stack scrolls; mouths must AutoSize to children
            Margin = new Padding(BlockTheme.MouthIndent, BlockTheme.BlockSpacing, BlockTheme.MouthIndent, BlockTheme.BlockSpacing),
            BackColor = Color.FromArgb(14, 22, 36),
        };
        _mouths.Add(mouth);
        BodyPanel.Controls.Add(mouth);
        return mouth;
    }
}
