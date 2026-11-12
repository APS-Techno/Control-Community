using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core.Classes;
using APS.RecipeModel.Controls;
using APS.RecipeModel.Model;
using APS.RecipeModel.Serialization;

namespace APS.SystemControl.Forms;

public class frmRecipeSelector : Form
{
    private readonly ListView lstFiles;
    private readonly ColumnHeader colName;
    private readonly ColumnHeader colModified;
    private readonly SplitContainer _splitMain;
    private readonly RecipeParamForm paramForm;
    private readonly BufferedRichTextBox txtPreview;
    private readonly Panel previewPanel;
    private readonly Button btnTogglePreview;
    private readonly Button btnOK;
    private readonly Button btnCancel;
    private readonly Label lblDir;

    private string _directory = string.Empty;
    private IReadOnlyList<RecipeEntry> _entries = [];
    private RecipeFile? _currentRecipe;
    private int _listWidth;

    /// <summary>Recipe chosen last time this dialog was confirmed, and the sort the operator
    /// left it in; both restored on reopen for the lifetime of the process.</summary>
    private static string? s_lastSelectedPath;
    private static RecipeSortKey s_sortKey = RecipeSortKey.Name;
    private static bool s_sortAscending = true;

    private HintManager _hints = null!;

    public string SelectedFilePath { get; private set; } = string.Empty;
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    public frmRecipeSelector()
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Select Recipe";
        Size = new Size(DpiScale.Px(860), DpiScale.Px(580));
        MinimumSize = new Size(DpiScale.Px(640), DpiScale.Px(450));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;

        lblDir = new Label
        {
            Dock = DockStyle.Top,
            Height = DpiScale.Px(24),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(DpiScale.Px(4), 0, 0, 0)
        };
        Controls.Add(lblDir);

        var panelButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Height = DpiScale.Px(40),
            Padding = new Padding(DpiScale.Px(8), DpiScale.Px(6), DpiScale.Px(8), DpiScale.Px(6))
        };

        // Default Button height is a fixed 23 physical px and does not scale
        // with DPI — set it explicitly or the scaled font clips.
        var btnSize = new Size(DpiScale.Px(80), DpiScale.Px(26));
        btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = btnSize };
        btnOK = new Button { Text = "OK", Size = btnSize, Enabled = false };
        btnOK.Click += BtnOK_Click;

        btnTogglePreview = new Button { Text = "Show Script", Size = new Size(DpiScale.Px(100), DpiScale.Px(26)) };
        btnTogglePreview.Click += BtnTogglePreview_Click;

        panelButtons.Controls.Add(btnCancel);
        panelButtons.Controls.Add(btnOK);
        panelButtons.Controls.Add(btnTogglePreview);
        Controls.Add(panelButtons);

        AcceptButton = btnOK;
        CancelButton = btnCancel;

        previewPanel = new Panel { Dock = DockStyle.Bottom, Height = DpiScale.Px(180), Visible = false };

        txtPreview = new BufferedRichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 10f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both
        };

        var lblPreview = new Label
        {
            Text = "Script Preview",
            Dock = DockStyle.Top,
            Font = ScaledFont.Create(9f, FontStyle.Bold),
            Height = DpiScale.Px(20),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(DpiScale.Px(4), 0, 0, 0)
        };

        previewPanel.Controls.Add(txtPreview);
        previewPanel.Controls.Add(lblPreview);
        Controls.Add(previewPanel);

        var splitMain = _splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 100,
            Panel2MinSize = 100
        };

        // Recipe list: Name + Modified columns, sortable by either (click the header),
        // grouped by sub-folder.
        lstFiles = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            ShowGroups = true,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            BorderStyle = BorderStyle.None,
            Font = ScaledFont.Create(10f),
            // An empty image list is the WinForms way to set a Details row height.
            SmallImageList = new ImageList { ImageSize = new Size(1, DpiScale.Px(24)) },
        };
        colName = lstFiles.Columns.Add("Recipe", DpiScale.Px(260));
        colModified = lstFiles.Columns.Add("Modified", DpiScale.Px(135));
        lstFiles.ColumnClick += LstFiles_ColumnClick;
        lstFiles.SelectedIndexChanged += LstFiles_SelectedIndexChanged;
        lstFiles.MouseDoubleClick += LstFiles_MouseDoubleClick;
        // ClientSizeChanged also fires when the vertical scrollbar appears or goes away.
        lstFiles.ClientSizeChanged += (_, _) => FitNameColumn();

        // 1 px frame around the list, in the panel border colour.
        var listFrame = new Panel { Dock = DockStyle.Fill, Padding = new Padding(1), BackColor = VacuumTheme.PanelBorder,
                                    Margin = new Padding(0) };
        listFrame.Controls.Add(lstFiles);
        splitMain.Panel1.Controls.Add(listFrame);

        paramForm = new RecipeParamForm { Dock = DockStyle.Fill };
        splitMain.Panel2.Controls.Add(paramForm);

        Controls.Add(splitMain);
        lblDir.BringToFront();
        splitMain.BringToFront();

        // Set splitter after layout so Width is known
        Load += (_, _) => ApplySplitter();

        DarkModeHelper.FixControls(this);
        // After FixControls: it owner-draws the header and asks for default item drawing;
        // our handlers run after its and take over the rows (banding, selection band).
        lstFiles.DrawItem += LstFiles_DrawItem;
        lstFiles.DrawSubItem += LstFiles_DrawSubItem;

        _hints = new HintManager(this);
        _hints.Set(lstFiles,         "Available recipes, grouped by folder. Click a column header to sort by name or date; select one to preview its parameters and script.");
        _hints.Set(paramForm,        "Recipe parameters — fill in any required values before confirming with OK.");
        _hints.Set(btnTogglePreview, "Toggle the raw recipe script preview pane.");
        _hints.Set(txtPreview,       "Read-only preview of the recipe's script steps (syntax-highlighted).");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    public void SetDirectory(string directoryPath)
    {
        _directory = directoryPath;
        lblDir.Text = $"Directory: {directoryPath}";
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        _entries = Directory.Exists(_directory)
            ? RecipeCatalog.Build(Directory.GetFiles(_directory, "*.rcpx", SearchOption.AllDirectories)
                                           .Select(f => (Path.GetRelativePath(_directory, f), File.GetLastWriteTime(f))))
            : [];
        FitListWidth();
        Repopulate(selectPath: s_lastSelectedPath);
    }

    /// <summary>Rebuild the items in the current sort, keeping the folder groups, and
    /// select <paramref name="selectPath"/> if present, else the first root recipe.</summary>
    private void Repopulate(string? selectPath)
    {
        colName.Text = "Recipe" + SortMark(RecipeSortKey.Name);
        colModified.Text = "Modified" + SortMark(RecipeSortKey.Modified);

        lstFiles.BeginUpdate();
        try
        {
            lstFiles.Items.Clear();
            lstFiles.Groups.Clear();
            var groups = new Dictionary<string, ListViewGroup>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in RecipeCatalog.Folders(_entries))
            {
                // Root recipes get a named group too — otherwise WinForms shows them under "Default".
                var g = new ListViewGroup(folder.Length == 0 ? Path.GetFileName(_directory) : folder)
                        { HeaderAlignment = HorizontalAlignment.Left,
                          CollapsedState = ListViewGroupCollapsedState.Expanded };
                groups[folder] = g;
                lstFiles.Groups.Add(g);
            }
            foreach (var e in RecipeCatalog.Sort(_entries, s_sortKey, s_sortAscending))
            {
                var item = new ListViewItem(e.Name) { Tag = e };
                item.SubItems.Add(e.Modified.ToString("yyyy-MM-dd HH:mm"));
                if (groups.TryGetValue(e.Folder, out var g)) item.Group = g;
                lstFiles.Items.Add(item);
            }
        }
        finally { lstFiles.EndUpdate(); }
        FitNameColumn();

        var initial = lstFiles.Items.Cast<ListViewItem>()
            .FirstOrDefault(i => selectPath is not null && i.Tag is RecipeEntry e
                                 && string.Equals(e.RelativePath, selectPath, StringComparison.OrdinalIgnoreCase))
            ?? lstFiles.Items.Cast<ListViewItem>().FirstOrDefault(i => i.Tag is RecipeEntry { Folder.Length: 0 })
            ?? (lstFiles.Items.Count > 0 ? lstFiles.Items[0] : null);
        if (initial is not null)
        {
            initial.Selected = true;
            initial.Focused = true;
            initial.EnsureVisible();
        }
    }

    private static string SortMark(RecipeSortKey key) =>
        key != s_sortKey ? "" : s_sortAscending ? "  ▲" : "  ▼";

    private void LstFiles_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        var key = e.Column == colModified.Index ? RecipeSortKey.Modified : RecipeSortKey.Name;
        if (key == s_sortKey)
            s_sortAscending = !s_sortAscending;
        else
        {
            s_sortKey = key;
            s_sortAscending = key == RecipeSortKey.Name;   // names A→Z, dates newest first
        }
        var keep = (lstFiles.SelectedItems.Count > 0 ? lstFiles.SelectedItems[0].Tag as RecipeEntry : null)?.RelativePath;
        Repopulate(selectPath: keep);
    }

    /// <summary>Size the list pane to the longest recipe name plus the date column (capped
    /// at half the dialog) so names are not truncated behind the splitter.</summary>
    private void FitListWidth()
    {
        int widest = 0;
        foreach (var e in _entries)
            widest = Math.Max(widest, TextRenderer.MeasureText(e.Name, lstFiles.Font).Width);
        widest = Math.Max(widest, DpiScale.Px(160));
        _listWidth = Math.Clamp(widest + DpiScale.Px(24) + colModified.Width + DpiScale.Px(30), DpiScale.Px(300), Width / 2);
        if (IsHandleCreated) ApplySplitter();
    }

    private void ApplySplitter()
    {
        try { _splitMain.SplitterDistance = _listWidth > 0 ? _listWidth : DpiScale.Px(300); } catch { }
        FitNameColumn();
    }

    private void FitNameColumn()
    {
        int w = lstFiles.ClientSize.Width - colModified.Width - DpiScale.Px(4);
        if (w > DpiScale.Px(60)) colName.Width = w;
    }

    private void LstFiles_DrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
        bool selected = e.Item.Selected;
        Color bg = selected ? VacuumTheme.LerpColor(VacuumTheme.PanelField, VacuumTheme.GlowBlue, 0.35f)
                 : e.ItemIndex % 2 == 1 ? VacuumTheme.PanelField
                 : VacuumTheme.PanelBackground;
        using var b = new SolidBrush(bg);
        // Band only the row the control hands us: painting out to ClientSize.Width ran over
        // the vertical scrollbar. The name column is fitted to the client width instead.
        e.Graphics.FillRectangle(b, e.Bounds);
    }

    private void LstFiles_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        e.DrawDefault = false;
        Color fg = e.Item?.Selected == true ? Color.White : e.ColumnIndex == 0 ? VacuumTheme.TextPrimary : VacuumTheme.TextDim;
        var rect = new Rectangle(e.Bounds.X + DpiScale.Px(6), e.Bounds.Y, e.Bounds.Width - DpiScale.Px(8), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, lstFiles.Font, rect, fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    private void LstFiles_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstFiles.SelectedItems.Count == 0 || lstFiles.SelectedItems[0].Tag is not RecipeEntry entry)
        {
            SelectedFilePath = string.Empty;
            btnOK.Enabled = false;
            return;
        }

        string fullPath = Path.Combine(_directory, entry.RelativePath);
        if (!File.Exists(fullPath)) { btnOK.Enabled = false; return; }

        try
        {
            _currentRecipe = RecipeSerializer.Load(fullPath);
            paramForm.LoadRecipe(_currentRecipe);
            txtPreview.Text = _currentRecipe.Script;
            SelectedFilePath = fullPath;
            btnOK.Enabled = true;
        }
        catch (Exception ex)
        {
            ThemedMessageBox.Show(this, $"Error loading recipe: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            btnOK.Enabled = false;
        }
    }

    private void LstFiles_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        // Double-clicking a recipe confirms it; a group header or empty space does nothing.
        if (lstFiles.HitTest(e.Location).Item is not null && btnOK.Enabled) BtnOK_Click(sender, e);
    }

    private void BtnTogglePreview_Click(object? sender, EventArgs e)
    {
        previewPanel.Visible = !previewPanel.Visible;
        btnTogglePreview.Text = previewPanel.Visible ? "Hide Script" : "Show Script";
    }

    private void BtnOK_Click(object? sender, EventArgs e)
    {
        Parameters.Clear();
        foreach (var kvp in paramForm.GetParameterValues())
            Parameters[kvp.Key] = kvp.Value;
        s_lastSelectedPath = (lstFiles.SelectedItems.Count > 0 ? lstFiles.SelectedItems[0].Tag as RecipeEntry : null)?.RelativePath;

        DialogResult = DialogResult.OK;
        Close();
    }
}
