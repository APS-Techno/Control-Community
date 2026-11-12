using APS.Controls.Theme;

public partial class frmFindReplace : Form
{
    public string FindText => txtFind.Text;
    public string ReplaceText => txtReplace.Text;
    private HintManager _hints = null!;

    public event EventHandler? FindNextClicked;
    public event EventHandler? ReplaceClicked;
    public event EventHandler? ReplaceAllClicked;

    public frmFindReplace()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        txtFind = new TextBox();
        txtReplace = new TextBox();
        btnFindNext = new Button();
        btnReplace = new Button();
        btnReplaceAll = new Button();
        btnCancel = new Button();
        SuspendLayout();
        txtFind.Location = new Point(DpiScale.Px(12), DpiScale.Px(12));
        txtFind.Name = "txtFind";
        txtFind.Size = new Size(DpiScale.Px(330), DpiScale.Px(23));
        txtFind.TabIndex = 0;
        txtReplace.Location = new Point(DpiScale.Px(12), DpiScale.Px(38));
        txtReplace.Name = "txtReplace";
        txtReplace.Size = new Size(DpiScale.Px(330), DpiScale.Px(23));
        txtReplace.TabIndex = 1;
        btnFindNext.Location = new Point(DpiScale.Px(12), DpiScale.Px(64));
        btnFindNext.Name = "btnFindNext";
        btnFindNext.Size = new Size(DpiScale.Px(78), DpiScale.Px(26));
        btnFindNext.TabIndex = 2;
        btnFindNext.Text = "Find Next";
        btnFindNext.UseVisualStyleBackColor = true;
        btnFindNext.Click += btnFindNext_Click;
        btnReplace.Location = new Point(DpiScale.Px(96), DpiScale.Px(64));
        btnReplace.Name = "btnReplace";
        btnReplace.Size = new Size(DpiScale.Px(78), DpiScale.Px(26));
        btnReplace.TabIndex = 3;
        btnReplace.Text = "Replace";
        btnReplace.UseVisualStyleBackColor = true;
        btnReplace.Click += btnReplace_Click;
        // Wider than the other buttons so "Replace All" does not truncate at 144 DPI.
        btnReplaceAll.Location = new Point(DpiScale.Px(180), DpiScale.Px(64));
        btnReplaceAll.Name = "btnReplaceAll";
        btnReplaceAll.Size = new Size(DpiScale.Px(90), DpiScale.Px(26));
        btnReplaceAll.TabIndex = 4;
        btnReplaceAll.Text = "Replace All";
        btnReplaceAll.UseVisualStyleBackColor = true;
        btnReplaceAll.Click += btnReplaceAll_Click;
        btnCancel.Location = new Point(DpiScale.Px(276), DpiScale.Px(64));
        btnCancel.Name = "btnCancel";
        btnCancel.Size = new Size(DpiScale.Px(78), DpiScale.Px(26));
        btnCancel.TabIndex = 5;
        btnCancel.Text = "Cancel";
        btnCancel.UseVisualStyleBackColor = true;
        btnCancel.Click += btnCancel_Click;
        ClientSize = new Size(DpiScale.Px(366), DpiScale.Px(102));
        Controls.Add(btnCancel);
        Controls.Add(btnReplaceAll);
        Controls.Add(btnReplace);
        Controls.Add(btnFindNext);
        Controls.Add(txtReplace);
        Controls.Add(txtFind);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Name = "frmFindReplace";
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Find and Replace";
        ResumeLayout(false);
        PerformLayout();

        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(txtFind,      "Text to search for in the recipe script.");
        _hints.Set(txtReplace,   "Replacement text — used by Replace and Replace All.");
        _hints.Set(btnFindNext,  "Find the next occurrence of the search text from the current caret position.");
        _hints.Set(btnReplace,   "Replace the currently selected occurrence and advance to the next match.");
        _hints.Set(btnReplaceAll,"Replace all occurrences of the search text in the entire script.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    private void btnFindNext_Click(object? sender, EventArgs e)
    {
        FindNextClicked?.Invoke(this, EventArgs.Empty);
    }

    private void btnReplace_Click(object? sender, EventArgs e)
    {
        ReplaceClicked?.Invoke(this, EventArgs.Empty);
    }

    private void btnReplaceAll_Click(object? sender, EventArgs e)
    {
        ReplaceAllClicked?.Invoke(this, EventArgs.Empty);
    }

    private void btnCancel_Click(object? sender, EventArgs e)
    {
        this.Close();
    }

    private TextBox txtFind = null!;
    private TextBox txtReplace = null!;
    private Button btnFindNext = null!;
    private Button btnReplace = null!;
    private Button btnReplaceAll = null!;
    private Button btnCancel = null!;
}
