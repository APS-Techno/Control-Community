namespace APS.SystemControl.Forms;

partial class frmCrashReport
{
    private System.ComponentModel.IContainer components = null;
    private Label lblClassName = null!;
    private Label lblMessage = null!;
    private TextBox txtReport = null!;
    private Button btnCopy = null!;
    private Button btnOpenLog = null!;
    private Button btnContinue = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        lblClassName = new Label();
        lblMessage = new Label();
        txtReport = new TextBox();
        btnCopy = new Button();
        btnOpenLog = new Button();
        btnContinue = new Button();
        SuspendLayout();

        // lblClassName
        lblClassName.Location = new Point(12, 12);
        lblClassName.AutoSize = true;
        lblClassName.Font = new Font("Segoe UI", 11F, FontStyle.Bold);

        // lblMessage
        lblMessage.Location = new Point(12, 36);
        lblMessage.Size = new Size(560, 40);
        lblMessage.Font = new Font("Segoe UI", 9F);

        // txtReport
        txtReport.Location = new Point(12, 84);
        txtReport.Size = new Size(560, 280);
        txtReport.Multiline = true;
        txtReport.ReadOnly = true;
        txtReport.ScrollBars = ScrollBars.Vertical;
        txtReport.Font = new Font("Consolas", 9F);

        // btnCopy
        btnCopy.Location = new Point(12, 376);
        btnCopy.Size = new Size(96, 28);
        btnCopy.Text = "Copy";
        btnCopy.Click += BtnCopy_Click;

        // btnOpenLog
        btnOpenLog.Location = new Point(116, 376);
        btnOpenLog.Size = new Size(96, 28);
        btnOpenLog.Text = "Open Log";
        btnOpenLog.Click += BtnOpenLog_Click;

        // btnContinue
        btnContinue.Location = new Point(476, 376);
        btnContinue.Size = new Size(96, 28);
        btnContinue.Text = "Continue";
        btnContinue.DialogResult = DialogResult.OK;

        // frmCrashReport
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(584, 416);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "APS Control – Unhandled Exception";

        Controls.Add(lblClassName);
        Controls.Add(lblMessage);
        Controls.Add(txtReport);
        Controls.Add(btnCopy);
        Controls.Add(btnOpenLog);
        Controls.Add(btnContinue);
        AcceptButton = btnContinue;
        ResumeLayout(false);
        PerformLayout();
    }
}
