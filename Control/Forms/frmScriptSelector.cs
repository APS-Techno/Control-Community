using System.Text.RegularExpressions;
using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Forms;

public class frmScriptSelector : Form
{
    private readonly ListBox lstFiles;
    private readonly BufferedRichTextBox txtPreview;
    private readonly Button btnOK;
    private readonly Button btnCancel;
    private readonly Label lblDir;

    private string _directory = string.Empty;
    private string _filter = "*.recipe";
    private HintManager _hints = null!;

    /// <summary>Valid only when DialogResult == OK.</summary>
    public string SelectedFilePath { get; private set; } = string.Empty;

    public frmScriptSelector()
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Select Script";
        Size = new Size(DpiScale.Px(680), DpiScale.Px(460));
        MinimumSize = new Size(DpiScale.Px(500), DpiScale.Px(340));
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

        btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = new Size(DpiScale.Px(80), DpiScale.Px(28)) };
        btnOK = new Button { Text = "OK", Size = new Size(DpiScale.Px(80), DpiScale.Px(28)), Enabled = false };
        btnOK.Click += BtnOK_Click;

        panelButtons.Controls.Add(btnCancel);
        panelButtons.Controls.Add(btnOK);
        Controls.Add(panelButtons);

        AcceptButton = btnOK;
        CancelButton = btnCancel;

        var splitMain = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = DpiScale.Px(200),
            Panel1MinSize = DpiScale.Px(120),
            Panel2MinSize = DpiScale.Px(200)
        };

        lstFiles = new ListBox
        {
            Dock = DockStyle.Fill,
            Font = ScaledFont.Create(10f),
            IntegralHeight = false
        };
        lstFiles.SelectedIndexChanged += LstFiles_SelectedIndexChanged;
        lstFiles.DoubleClick += LstFiles_DoubleClick;
        splitMain.Panel1.Controls.Add(lstFiles);

        txtPreview = new BufferedRichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font("Consolas", 10f),
            WordWrap = false,
            ScrollBars = RichTextBoxScrollBars.Both,
            BackColor = SystemColors.Window
        };
        splitMain.Panel2.Controls.Add(txtPreview);

        Controls.Add(splitMain);

        lblDir.BringToFront();
        splitMain.BringToFront();

        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(lstFiles,    "List of available script files. Select one to preview its contents.");
        _hints.Set(txtPreview,  "Read-only preview of the selected script (syntax-highlighted). Keywords, comments, and variables are colour-coded.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    public void SetDirectory(string directoryPath, string fileFilter = "*.recipe")
    {
        _directory = directoryPath;
        _filter = fileFilter;
        lblDir.Text = $"Directory: {directoryPath}";
        RefreshFileList();
    }

    private void RefreshFileList()
    {
        lstFiles.Items.Clear();
        if (!Directory.Exists(_directory)) return;

        var files = Directory.GetFiles(_directory, _filter, SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            string relative = Path.GetRelativePath(_directory, f);
            lstFiles.Items.Add(relative);
        }
    }

    private void LstFiles_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (lstFiles.SelectedItem is not string relPath) return;

        string fullPath = Path.Combine(_directory, relPath);
        if (!File.Exists(fullPath))
        {
            txtPreview.Text = "(file not found)";
            btnOK.Enabled = false;
            return;
        }

        txtPreview.Text = File.ReadAllText(fullPath);
        btnOK.Enabled = true;
        SelectedFilePath = fullPath;

        ApplyPreviewHighlighting();
    }

    private void LstFiles_DoubleClick(object? sender, EventArgs e)
    {
        if (btnOK.Enabled)
            BtnOK_Click(sender, e);
    }

    private static readonly string[] Keywords =
    {
        "valve", "mfc", "pump", "stage", "delay", "loop", "endloop", "isolate",
        "var", "function", "endfunction", "bias", "set", "warmup",
        "pwait", "swait", "state", "check", "holder", "shutter", "scan",
        "scanR", "deposit", "rotation", "message", "wait", "begin", "end"
    };

    private void ApplyPreviewHighlighting()
    {
        if (txtPreview.TextLength == 0) return;

        int sel = txtPreview.SelectionStart;
        txtPreview.SuspendLayout();

        txtPreview.SelectAll();
        txtPreview.SelectionColor = txtPreview.ForeColor;

        // Colors must stay readable on the FixControls-darkened (#0D1B2A) preview pane;
        // palette matches RecipeModel ScriptHighlighter.
        foreach (string kw in Keywords)
        {
            var regex = new Regex($@"\b{Regex.Escape(kw)}\b", RegexOptions.IgnoreCase);
            foreach (Match m in regex.Matches(txtPreview.Text))
            {
                txtPreview.Select(m.Index, m.Length);
                txtPreview.SelectionColor = Color.FromArgb(86, 156, 214);
            }
        }

        var commentRegex = new Regex(@"(#|//).*$", RegexOptions.Multiline);
        foreach (Match m in commentRegex.Matches(txtPreview.Text))
        {
            txtPreview.Select(m.Index, m.Length);
            txtPreview.SelectionColor = Color.FromArgb(106, 153, 85);
        }

        var varRegex = new Regex(@"\$\w+");
        foreach (Match m in varRegex.Matches(txtPreview.Text))
        {
            txtPreview.Select(m.Index, m.Length);
            txtPreview.SelectionColor = Color.FromArgb(190, 132, 255);
        }

        txtPreview.Select(sel, 0);
        txtPreview.ResumeLayout();
    }

    private void BtnOK_Click(object? sender, EventArgs e)
    {
        DialogResult = DialogResult.OK;
        Close();
    }
}
