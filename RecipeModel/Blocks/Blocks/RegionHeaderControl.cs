using APS.Controls.Theme;

namespace APS.RecipeModel.Blocks;

public sealed class RegionHeaderControl : Panel
{
    private readonly Label _glyph;
    private readonly Label _title;
    public event Action<string>? Renamed;
    public event Action? RemoveRequested;

    public RegionHeaderControl(string name, int stepCount, bool collapsed, int depth, Action onToggle)
    {
        AutoSize = false;
        Height = BlockTheme.HeaderHeight;
        Margin = new Padding(depth * BlockTheme.MouthIndent, BlockTheme.BlockSpacing, 0, 0);
        BackColor = System.Drawing.Color.FromArgb(30, 44, 66);

        _glyph = new Label
        {
            Text = collapsed ? "▸" : "▾",
            AutoSize = false,
            Width = BlockTheme.Scale(16),
            Dock = DockStyle.Left,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            ForeColor = BlockTheme.BlockTextOnCat,
        };
        _title = new Label
        {
            Text = $"{(string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name)}  —  {stepCount} steps",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            ForeColor = BlockTheme.LabelForeground,
            Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold),
        };
        Controls.Add(_title);
        Controls.Add(_glyph);

        void toggle(object? s, EventArgs e) => onToggle();
        _glyph.Click += toggle;
        Click += toggle;
        _title.MouseDoubleClick += (_, _) => BeginRename(name);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Remove region", null, (_, _) => RemoveRequested?.Invoke());
        // ContextMenuStrip doesn't cascade to children in WinForms — a right-click that
        // lands on the glyph / title Label needs the CMS set explicitly.
        ContextMenuStrip = menu;
        _glyph.ContextMenuStrip = menu;
        _title.ContextMenuStrip = menu;

        HandleCreated += (_, _) => DarkModeHelper.FixControls(this);
    }

    private void BeginRename(string current)
    {
        using var dlg = new RenameRegionDialog(current);
        if (dlg.ShowDialog(this) == DialogResult.OK) Renamed?.Invoke(dlg.RegionName);
    }
}

public sealed class RenameRegionDialog : Form
{
    private readonly TextBox _text;
    public string RegionName => _text.Text.Trim();
    public RenameRegionDialog(string current)
    {
        Text = "Rename region";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new System.Drawing.Size(BlockTheme.Scale(280), BlockTheme.Scale(90));
        _text = new TextBox { Text = current, Left = BlockTheme.Scale(12), Top = BlockTheme.Scale(12),
            Width = ClientSize.Width - BlockTheme.Scale(24) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK,
            Left = ClientSize.Width - BlockTheme.Scale(176), Top = BlockTheme.Scale(48), Width = BlockTheme.Scale(76) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel,
            Left = ClientSize.Width - BlockTheme.Scale(92), Top = BlockTheme.Scale(48), Width = BlockTheme.Scale(80) };
        Controls.AddRange(new Control[] { _text, ok, cancel });
        AcceptButton = ok; CancelButton = cancel;
        HandleCreated += (_, _) => APS.Controls.Theme.DarkModeHelper.FixControls(this);
    }
}
