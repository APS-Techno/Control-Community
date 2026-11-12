using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

public partial class frmCrashReport : Form
{
    private readonly string _logPath;

    public frmCrashReport(string className, string message, string fullReport, string logPath)
    {
        InitializeComponent();
        DarkModeHelper.FixControls(this);
        lblClassName.Text = className;
        lblMessage.Text = message;
        txtReport.Text = fullReport;
        _logPath = logPath;
    }

    private void BtnCopy_Click(object? sender, EventArgs e)
    {
        try { Clipboard.SetText(txtReport.Text); }
        catch { /* clipboard contention — ignore */ }
    }

    private void BtnOpenLog_Click(object? sender, EventArgs e)
    {
        try
        {
            if (File.Exists(_logPath))
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _logPath,
                    UseShellExecute = true
                });
        }
        catch { /* shell-execute failure — silent */ }
    }
}
