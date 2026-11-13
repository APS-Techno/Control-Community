using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

partial class frmAppSettings
{
    private System.ComponentModel.IContainer components = null!;
    private TableLayoutPanel grid = null!;
    private Label lblConfigDir = null!;
    private TextBox txtConfigDir = null!;
    private Button btnBrowseConfigDir = null!;
    private Label lblPrefix = null!;
    private ComboBox cboPrefix = null!;
    private Label lblPrefixStatus = null!;
    private Label lblWorkDir = null!;
    private TextBox txtWorkDir = null!;
    private Button btnBrowseWorkDir = null!;
    private Label lblHWDir = null!;
    private TextBox txtHWDir = null!;
    private Button btnBrowseHWDir = null!;
    private Label lblGUIDir = null!;
    private TextBox txtGUIDir = null!;
    private Button btnBrowseGUIDir = null!;
    private Label lblRecipesDir = null!;
    private TextBox txtRecipesDir = null!;
    private Button btnBrowseRecipesDir = null!;
    private Label lblLogsDir = null!;
    private TextBox txtLogsDir = null!;
    private Button btnBrowseLogsDir = null!;
    private Label lblDataDir = null!;
    private TextBox txtDataDir = null!;
    private Button btnBrowseDataDir = null!;
    private Label lblRecipeLog = null!;
    private CheckBox chkLogUserRecipes = null!;
    private CheckBox chkLogEmbeddedRecipes = null!;
    private FlowLayoutPanel buttonRow = null!;
    private Button btnOK = null!;
    private Button btnApply = null!;
    private Button btnCancel = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        SuspendLayout();

        Text = "App Settings";
        Width = DpiScale.Px(560);
        Height = DpiScale.Px(450);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;

        grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 11,
            Padding = new Padding(DpiScale.Px(12)),
            AutoSize = false,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiScale.Px(150)));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiScale.Px(32)));
        for (int i = 0; i < 11; i++)
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: ConfigDir
        lblConfigDir = MakeLabel("Config dir:");
        txtConfigDir = MakeTextBox();
        btnBrowseConfigDir = MakeBrowseButton();
        grid.Controls.Add(lblConfigDir, 0, 0);
        grid.Controls.Add(txtConfigDir, 1, 0);
        grid.Controls.Add(btnBrowseConfigDir, 2, 0);

        // Row 1: Prefix combo
        lblPrefix = MakeLabel("Project (Prefix):");
        cboPrefix = new DarkComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
        };
        grid.Controls.Add(lblPrefix, 0, 1);
        grid.Controls.Add(cboPrefix, 1, 1);

        // Row 2: Prefix status (advisory)
        lblPrefixStatus = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = DpiScale.Px(20),
            Text = string.Empty,
            ForeColor = Color.Gray,
        };
        grid.Controls.Add(lblPrefixStatus, 1, 2);

        // Row 3: WorkDir
        lblWorkDir = MakeLabel("Work dir:");
        txtWorkDir = MakeTextBox();
        txtWorkDir.PlaceholderText = "(leave blank for default — app base directory)";
        btnBrowseWorkDir = MakeBrowseButton();
        grid.Controls.Add(lblWorkDir, 0, 3);
        grid.Controls.Add(txtWorkDir, 1, 3);
        grid.Controls.Add(btnBrowseWorkDir, 2, 3);

        // Row 4: HWDir
        lblHWDir = MakeLabel("HW dir:");
        txtHWDir = MakeTextBox();
        txtHWDir.PlaceholderText = "(leave blank for default — WorkDir/HW_LIB/<Prefix>)";
        btnBrowseHWDir = MakeBrowseButton();
        grid.Controls.Add(lblHWDir, 0, 4);
        grid.Controls.Add(txtHWDir, 1, 4);
        grid.Controls.Add(btnBrowseHWDir, 2, 4);

        // Row 5: GUIDir
        lblGUIDir = MakeLabel("GUI dir:");
        txtGUIDir = MakeTextBox();
        txtGUIDir.PlaceholderText = "(leave blank for default — WorkDir/GUI_LIB/<Prefix>)";
        btnBrowseGUIDir = MakeBrowseButton();
        grid.Controls.Add(lblGUIDir, 0, 5);
        grid.Controls.Add(txtGUIDir, 1, 5);
        grid.Controls.Add(btnBrowseGUIDir, 2, 5);

        // Row 6: RecipesDir
        lblRecipesDir = MakeLabel("Recipe folder:");
        txtRecipesDir = MakeTextBox();
        txtRecipesDir.PlaceholderText = "(leave blank for default — ConfigDir/Recipes)";
        btnBrowseRecipesDir = MakeBrowseButton();
        grid.Controls.Add(lblRecipesDir, 0, 6);
        grid.Controls.Add(txtRecipesDir, 1, 6);
        grid.Controls.Add(btnBrowseRecipesDir, 2, 6);

        // Row 7: LogsDir (text logs)
        lblLogsDir = MakeLabel("Logs folder:");
        txtLogsDir = MakeTextBox();
        txtLogsDir.PlaceholderText = "(leave blank for default — WorkDir/Logs)";
        btnBrowseLogsDir = MakeBrowseButton();
        grid.Controls.Add(lblLogsDir, 0, 7);
        grid.Controls.Add(txtLogsDir, 1, 7);
        grid.Controls.Add(btnBrowseLogsDir, 2, 7);

        // Row 8: DataDir (TSV + snapshots)
        lblDataDir = MakeLabel("Data folder:");
        txtDataDir = MakeTextBox();
        txtDataDir.PlaceholderText = "(leave blank for default — WorkDir/Data)";
        btnBrowseDataDir = MakeBrowseButton();
        grid.Controls.Add(lblDataDir, 0, 8);
        grid.Controls.Add(txtDataDir, 1, 8);
        grid.Controls.Add(btnBrowseDataDir, 2, 8);

        // Row 9: Recipe logging — user recipes
        lblRecipeLog = MakeLabel("Recipe logging:");
        chkLogUserRecipes = new CheckBox
        {
            Text = "Log user recipes",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, DpiScale.Px(4), 0, 0),
        };
        grid.Controls.Add(lblRecipeLog, 0, 9);
        grid.Controls.Add(chkLogUserRecipes, 1, 9);

        // Row 10: Recipe logging — embedded recipes
        chkLogEmbeddedRecipes = new CheckBox
        {
            Text = "Log embedded recipes (SYSTEM_*, LOAD_*, …)",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 0, 0, DpiScale.Px(4)),
        };
        grid.Controls.Add(chkLogEmbeddedRecipes, 1, 10);

        // Button row
        buttonRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(DpiScale.Px(12), DpiScale.Px(6), DpiScale.Px(12), DpiScale.Px(12)),
            Height = DpiScale.Px(48),
            AutoSize = false,
        };
        // Default Button height is fixed physical px and does not scale with
        // DPI — set sizes explicitly or the scaled font clips.
        var btnSize = new Size(DpiScale.Px(90), DpiScale.Px(26));
        btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = btnSize };
        btnApply = new Button { Text = "Apply", Size = btnSize, Enabled = false };
        btnOK = new Button { Text = "OK", Size = btnSize };
        buttonRow.Controls.Add(btnCancel);
        buttonRow.Controls.Add(btnApply);
        buttonRow.Controls.Add(btnOK);

        Controls.Add(grid);
        Controls.Add(buttonRow);

        AcceptButton = btnOK;
        CancelButton = btnCancel;

        ResumeLayout(false);
        PerformLayout();
    }

    private static Label MakeLabel(string text) => new Label
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        AutoSize = false,
    };

    private static TextBox MakeTextBox() => new TextBox
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 4, 0, 4),
    };

    private static Button MakeBrowseButton() => new Button
    {
        Text = "…",
        Dock = DockStyle.Fill,
        Margin = new Padding(2, 4, 0, 4),
    };
}
