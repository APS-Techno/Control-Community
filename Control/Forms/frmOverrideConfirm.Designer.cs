using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

partial class frmOverrideConfirm
{
    private System.ComponentModel.IContainer components = null!;
    private Label ruleLabel = null!;
    private TextBox reasonBox = null!;
    private CheckBox persistBox = null!;
    private Button okButton = null!;
    private Button cancelButton = null!;
    private Label reasonHint = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        Text = "Disable this safety rule?";
        // Pixel values are 96-DPI logical, scaled once via DpiScale.Px
        // (fonts are point-sized and scale on their own).
        Width = DpiScale.Px(520); Height = DpiScale.Px(260);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false;

        ruleLabel = new Label
        {
            Location = new Point(DpiScale.Px(16), DpiScale.Px(16)),
            Size = new Size(DpiScale.Px(480), DpiScale.Px(40)),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            AutoEllipsis = true,
        };
        reasonHint = new Label
        {
            Location = new Point(DpiScale.Px(16), DpiScale.Px(64)),
            Size = new Size(DpiScale.Px(480), DpiScale.Px(18)),
            Text = "Reason (required if you Persist):",
        };
        reasonBox = new TextBox
        {
            Location = new Point(DpiScale.Px(16), DpiScale.Px(84)),
            Size = new Size(DpiScale.Px(480), DpiScale.Px(22)),
            PlaceholderText = "e.g. gauge drift — testing",
        };
        persistBox = new CheckBox
        {
            Location = new Point(DpiScale.Px(16), DpiScale.Px(118)),
            Size = new Size(DpiScale.Px(200), DpiScale.Px(22)),
            Text = "Persist across restart",
        };
        okButton = new Button
        {
            Location = new Point(DpiScale.Px(324), DpiScale.Px(170)),
            Size = new Size(DpiScale.Px(80), DpiScale.Px(28)),
            Text = "Disable",
            DialogResult = DialogResult.OK,
        };
        cancelButton = new Button
        {
            Location = new Point(DpiScale.Px(412), DpiScale.Px(170)),
            Size = new Size(DpiScale.Px(80), DpiScale.Px(28)),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
        };

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[] { ruleLabel, reasonHint, reasonBox, persistBox, okButton, cancelButton });
    }
}
