using APS.Controls.Theme;
using APS.SystemControl.Core;

namespace APS.SystemControl.Forms;

internal sealed partial class frmOverrideConfirm : Form
{
    public string Reason => reasonBox.Text.Trim();
    public bool Persist => persistBox.Checked;
    private HintManager _hints = null!;

    public frmOverrideConfirm(string ruleLabelText)
    {
        InitializeComponent();
        UIScaling.ApplyToForm(this);
        ruleLabel.Text = ruleLabelText;
        persistBox.CheckedChanged += (_, _) => UpdateOkState();
        reasonBox.TextChanged += (_, _) => UpdateOkState();
        UpdateOkState();
        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(reasonBox,  "Why you're overriding this rule; required when persisting, and audit-logged.");
        _hints.Set(persistBox, "Keep this override across Control restarts (requires a reason).");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hints?.Dispose();
        base.OnFormClosed(e);
    }

    private void UpdateOkState()
    {
        okButton.Enabled = !persistBox.Checked || reasonBox.Text.Trim().Length > 0;
    }
}
