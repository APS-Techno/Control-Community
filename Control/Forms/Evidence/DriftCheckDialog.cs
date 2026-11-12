using APS.Controls.Theme;
using APS.SystemControl.Core.Evidence;

namespace APS.SystemControl.Forms.Evidence;

/// <summary>Shows the re-run drift report. OK = proceed, Cancel = abort. Advisory only.</summary>
public sealed class DriftCheckDialog : Form
{
    private HintManager _hints = null!;

    public DriftCheckDialog(DriftReport report)
    {
        Text = report.HasWarnings ? "Conditions differ — proceed?" : "Re-run conditions";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ClientSize = new Size(DpiScale.Px(520), DpiScale.Px(320));

        var body = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Location = new Point(DpiScale.Px(12), DpiScale.Px(12)),
            Size = new Size(DpiScale.Px(496), DpiScale.Px(250)),
            Text = report.Items.Count == 0
                ? "No differences from the captured conditions."
                : string.Join(Environment.NewLine, report.Items.Select(i => $"[{i.Grade}] {i.Message}")),
        };
        var ok = new Button
        {
            Text = report.HasWarnings ? "Run anyway" : "Run",
            DialogResult = DialogResult.OK,
            Location = new Point(DpiScale.Px(300), DpiScale.Px(275)),
            Width = DpiScale.Px(120),
            Height = DpiScale.Px(28),
        };
        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(DpiScale.Px(428), DpiScale.Px(275)),
            Width = DpiScale.Px(80),
            Height = DpiScale.Px(28),
        };

        Controls.AddRange([body, ok, cancel]);
        AcceptButton = ok;
        CancelButton = cancel;
        DarkModeHelper.FixControls(this); // MANDATORY per project theming rule

        _hints = new HintManager(this);
        _hints.Set(body, "Drift report: differences between the original captured conditions and the current system state. Each line is prefixed with its severity grade.");
        _hints.Set(ok,   report.HasWarnings
            ? "Proceed with this re-run despite the listed condition differences. The drift report will be included in the evidence file."
            : "Conditions match — proceed with the re-run.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }
}
