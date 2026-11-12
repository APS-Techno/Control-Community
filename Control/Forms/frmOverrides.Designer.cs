using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

partial class frmOverrides
{
    private System.ComponentModel.IContainer components = null!;
    private Panel headerBand = null!;
    private Label headerLabel = null!;
    private DarkTabControl tabControl = null!;
    private TabPage tabAlarms = null!;
    private TabPage tabInterlocks = null!;
    private DataGridView gridAlarms = null!;
    private DataGridView gridInterlocks = null!;
    private TextBox filterBox = null!;
    private Button clearAllButton = null!;
    private Button closeButton = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();

        SuspendLayout();
        Text = "Alarm & Interlock Overrides";
        // Pixel values are 96-DPI logical, scaled once via DpiScale.Px.
        Width = DpiScale.Px(900);
        Height = DpiScale.Px(600);
        StartPosition = FormStartPosition.CenterParent;

        headerBand = new Panel
        {
            Dock = DockStyle.Top,
            Height = DpiScale.Px(44),
            BackColor = Color.FromArgb(180, 0, 0),
        };
        headerLabel = new Label
        {
            Dock = DockStyle.Fill,
            Text = "⚠  DISABLING A RULE BYPASSES A SAFETY CHECK. ALL TOGGLES ARE LOGGED.",
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        headerBand.Controls.Add(headerLabel);

        filterBox = new TextBox
        {
            Dock = DockStyle.Top,
            PlaceholderText = "Filter: substring of label or reason…",
        };

        tabControl = new DarkTabControl { Dock = DockStyle.Fill };
        tabAlarms = new TabPage("Alarms");
        tabInterlocks = new TabPage("Interlocks");
        gridAlarms = BuildGrid();
        gridInterlocks = BuildGrid();
        tabAlarms.Controls.Add(gridAlarms);
        tabInterlocks.Controls.Add(gridInterlocks);
        tabControl.TabPages.Add(tabAlarms);
        tabControl.TabPages.Add(tabInterlocks);

        clearAllButton = new Button { Text = "Clear all overrides", Width = DpiScale.Px(160), Height = DpiScale.Px(28) };
        closeButton    = new Button { Text = "Close",              Width = DpiScale.Px(90),  Height = DpiScale.Px(28) };

        var bottomBar = new Panel { Dock = DockStyle.Bottom, Height = DpiScale.Px(44) };
        clearAllButton.Location = new Point(DpiScale.Px(12), DpiScale.Px(8));
        closeButton.Location = new Point(Width - closeButton.Width - DpiScale.Px(24), DpiScale.Px(8));
        closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        bottomBar.Controls.Add(clearAllButton);
        bottomBar.Controls.Add(closeButton);

        // Docking order: bottom bar first (ends at bottom), then tab control (fills center),
        // filter box (above tab), then header (top). Add in reverse docking order so each new
        // dock pushes the previous inward.
        Controls.Add(tabControl);
        Controls.Add(filterBox);
        Controls.Add(headerBand);
        Controls.Add(bottomBar);

        closeButton.Click += (_, _) => Close();

        ResumeLayout(false);
    }

    private DataGridView BuildGrid()
    {
        var g = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Disabled", HeaderText = "Disabled", Width = DpiScale.Px(70) });
        g.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Persist",  HeaderText = "Persist",  Width = DpiScale.Px(60) });
        g.Columns.Add(new DataGridViewTextBoxColumn  { Name = "Label",    HeaderText = "Rule",     AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn  { Name = "Reason",   HeaderText = "Reason",   Width = DpiScale.Px(220) });
        g.Columns.Add(new DataGridViewTextBoxColumn  { Name = "When",     HeaderText = "Disabled at", Width = DpiScale.Px(130), ReadOnly = true });
        g.Columns.Add(new DataGridViewTextBoxColumn  { Name = "By",       HeaderText = "Disabled by", Width = DpiScale.Px(100), ReadOnly = true });
        return g;
    }
}
