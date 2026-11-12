using APS.Controls.Theme;

namespace APS.SystemEmulator.Setup;

partial class FrmOpenProject
{
    private System.ComponentModel.IContainer components = null;
    private System.Windows.Forms.Label _lblConfigsRoot;
    private System.Windows.Forms.TextBox _txtConfigsRoot;
    private System.Windows.Forms.Button _btnBrowse;
    private System.Windows.Forms.ListBox _lstProjects;
    private System.Windows.Forms.Button _btnOk;
    private System.Windows.Forms.Button _btnCancel;
    private System.Windows.Forms.CheckBox _chkSyncControl;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _lblConfigsRoot = new System.Windows.Forms.Label();
        _txtConfigsRoot = new System.Windows.Forms.TextBox();
        _btnBrowse = new System.Windows.Forms.Button();
        _lstProjects = new System.Windows.Forms.ListBox();
        _btnOk = new System.Windows.Forms.Button();
        _btnCancel = new System.Windows.Forms.Button();
        _chkSyncControl = new System.Windows.Forms.CheckBox();

        SuspendLayout();

        _lblConfigsRoot.AutoSize = true;
        _lblConfigsRoot.Location = new System.Drawing.Point(DpiScale.Px(12), DpiScale.Px(15));
        _lblConfigsRoot.Text = "Configs Root:";

        _txtConfigsRoot.Location = new System.Drawing.Point(DpiScale.Px(100), DpiScale.Px(12));
        _txtConfigsRoot.Size = new System.Drawing.Size(DpiScale.Px(360), DpiScale.Px(23));
        _txtConfigsRoot.ReadOnly = true;

        _btnBrowse.Location = new System.Drawing.Point(DpiScale.Px(470), DpiScale.Px(11));
        _btnBrowse.Size = new System.Drawing.Size(DpiScale.Px(90), DpiScale.Px(25));
        _btnBrowse.Text = "Browse...";
        _btnBrowse.Click += BtnBrowse_Click;

        _lstProjects.Location = new System.Drawing.Point(DpiScale.Px(12), DpiScale.Px(48));
        _lstProjects.Size = new System.Drawing.Size(DpiScale.Px(548), DpiScale.Px(230));
        _lstProjects.DoubleClick += LstProjects_DoubleClick;
        _lstProjects.SelectedIndexChanged += (_, _) => _btnOk.Enabled = _lstProjects.SelectedIndex >= 0;

        _btnOk.Location = new System.Drawing.Point(DpiScale.Px(380), DpiScale.Px(320));
        _btnOk.Size = new System.Drawing.Size(DpiScale.Px(85), DpiScale.Px(28));
        _btnOk.Text = "OK";
        _btnOk.DialogResult = System.Windows.Forms.DialogResult.OK;
        _btnOk.Enabled = false;
        _btnOk.Click += BtnOk_Click;

        _btnCancel.Location = new System.Drawing.Point(DpiScale.Px(475), DpiScale.Px(320));
        _btnCancel.Size = new System.Drawing.Size(DpiScale.Px(85), DpiScale.Px(28));
        _btnCancel.Text = "Cancel";
        _btnCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel;

        _chkSyncControl.Location = new System.Drawing.Point(DpiScale.Px(12), DpiScale.Px(287));
        _chkSyncControl.Size = new System.Drawing.Size(DpiScale.Px(360), DpiScale.Px(21));
        _chkSyncControl.Text = "Also point Control at this project";
        _chkSyncControl.Checked = true;
        _chkSyncControl.AutoSize = true;

        AcceptButton = _btnOk;
        CancelButton = _btnCancel;
        ClientSize = new System.Drawing.Size(DpiScale.Px(575), DpiScale.Px(362));
        Controls.Add(_lblConfigsRoot);
        Controls.Add(_txtConfigsRoot);
        Controls.Add(_btnBrowse);
        Controls.Add(_lstProjects);
        Controls.Add(_btnOk);
        Controls.Add(_btnCancel);
        Controls.Add(_chkSyncControl);
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
        Text = "Open Project";

        ResumeLayout(false);
        PerformLayout();
    }
}
