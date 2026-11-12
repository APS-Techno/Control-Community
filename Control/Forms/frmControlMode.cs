using APS.Controls.Theme;
using APS.SystemControl.Core;

namespace APS.SystemControl.Forms;

/// <summary>Offline / Local / Remote chooser (spec 2026-09-06 §5). Read-only without the
/// ServiceMenu permission. Never prompts about a running recipe: a switch does not touch it.</summary>
internal sealed class frmControlMode : Form
{
    private static readonly (ControlMode Mode, string Title, string Text)[] Rows =
    [
        (ControlMode.Local,   "Online-Local",  "Operator owns the machine. Panels, menus and Stream Deck work; MCP/host may only read."),
        (ControlMode.Remote,  "Online-Remote", "MCP/host may start recipes and command devices. Panels are locked except Abort, Isolate, Stop and fault-pause decisions."),
        (ControlMode.Offline, "Offline",       "Same as Local for the operator; the host sees the machine as offline (reserved for GEM)."),
    ];

    private readonly ControlModeService _svc;
    private readonly string _by;
    private readonly List<DarkRadioButton> _radios = [];

    public frmControlMode(ControlModeService svc, bool canSwitch, string by)
    {
        _svc = svc; _by = by;
        Text = "Control mode";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        // Width is fixed; the height is fitted to the content in OnLoad, once fonts and DPI
        // are final. A hard-coded height clipped the note above the buttons at 4K.
        ClientSize = new Size(DpiScale.Px(460), DpiScale.Px(250));
        Padding = new Padding(DpiScale.Px(12));

        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                                           ColumnCount = 1, RowCount = Rows.Length * 2 + 1 };
        _table = table;
        foreach (var (mode, title, text) in Rows)
        {
            var radio = new DarkRadioButton { Text = title, Tag = mode, AutoSize = true, Checked = svc.Mode == mode, Enabled = canSwitch };
            // Wrap width = client 460 − form padding 2×12 − label margin 22, so no text is clipped on the right.
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(DpiScale.Px(414), 0),
                                    Margin = new Padding(DpiScale.Px(22), 0, 0, DpiScale.Px(8)) };
            _radios.Add(radio);
            table.Controls.Add(radio); table.Controls.Add(label);
        }
        var note = new Label { AutoSize = true, ForeColor = VacuumTheme.TextDim,
            Text = canSwitch ? "A running recipe is not affected by switching." : "Switching needs the Engineer role." };
        table.Controls.Add(note);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
        // Height must scale too: a Button's default 23 px is a device value, so at 150-200 %
        // (4K) the DPI-scaled font no longer fits and the captions get cut off.
        var buttonSize = new Size(DpiScale.Px(88), DpiScale.Px(28));
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Enabled = canSwitch, Size = buttonSize };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Size = buttonSize };
        buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        _buttons = buttons;
        Controls.Add(table); Controls.Add(buttons);

        ok.Click += (_, _) =>
        {
            var chosen = _radios.FirstOrDefault(r => r.Checked);
            if (chosen is not null) _svc.Set((ControlMode)chosen.Tag!, _by);
        };
        DarkModeHelper.FixControls(this);
    }

    private readonly TableLayoutPanel _table;
    private readonly FlowLayoutPanel _buttons;

    /// <summary>Fit the client height to the auto-sized content: rows, note, a gap, the
    /// button strip and the form padding. Runs after the handle exists so the measured
    /// text heights are the DPI-scaled ones, then re-centres on the owner.</summary>
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        _table.PerformLayout();
        int height = _table.Bottom + DpiScale.Px(8) + _buttons.Height + Padding.Bottom;
        ClientSize = new Size(ClientSize.Width, height);
        if (StartPosition == FormStartPosition.CenterParent)
            CenterToParent();
    }
}
