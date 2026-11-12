using APS.Controls.Theme;
using APS.Controls.Vacuum;

namespace APS.Controls.Dialogs;

public class TransferModuleServiceDialog : Form
{
    /// <summary>None when closed via Close/Escape.</summary>
    public enum Action { None, Home, Abort, Transfer }

    public Action SelectedAction { get; private set; } = Action.None;

    /// <summary>0 = park.</summary>
    public byte SelectedSrc { get; private set; }

    /// <summary>0 = park.</summary>
    public byte SelectedDst { get; private set; }

    private readonly Label _stateLabel;
    private readonly Label _faceLabel;
    private readonly Label _wafLabel;
    private readonly Label _errLabel;

    private readonly DarkComboBox _srcCombo;
    private readonly DarkComboBox _dstCombo;

    private readonly Button _homeBtn;
    private readonly Button _abortBtn;
    private readonly Button _transferBtn;
    private readonly Button _closeBtn;

    /// <param name="faces">"park" (index 0) is added implicitly and always appears first in both dropdowns.</param>
    public TransferModuleServiceDialog(string robotId,
        IEnumerable<(string Name, byte Idx)> faces)
    {
        Text = $"Transfer Module — {robotId}";
        ClientSize = new Size(360, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        _stateLabel = MakeLabel(10,  10, "State:    —");
        _faceLabel  = MakeLabel(10,  30, "Face:     —");
        _wafLabel   = MakeLabel(10,  50, "HasWafer: —");
        _errLabel   = MakeLabel(10,  70, "Error:    —");

        var frLabel = MakeLabel(10, 110, "From:");
        frLabel.Width = 55;
        var toLabel = MakeLabel(10, 140, "To:");
        toLabel.Width = 55;

        _srcCombo = new DarkComboBox
        {
            Left = 72, Top = 108, Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };
        _dstCombo = new DarkComboBox
        {
            Left = 72, Top = 138, Width = 110,
            DropDownStyle = ComboBoxStyle.DropDownList,
        };

        _srcCombo.Items.Add(new FaceItem("park", 0));
        _dstCombo.Items.Add(new FaceItem("park", 0));
        foreach (var (n, i) in faces)
        {
            _srcCombo.Items.Add(new FaceItem(n, i));
            _dstCombo.Items.Add(new FaceItem(n, i));
        }
        _srcCombo.SelectedIndex = 0;
        _dstCombo.SelectedIndex = _dstCombo.Items.Count > 1 ? 1 : 0;

        _transferBtn = new Button
        {
            Left = 196, Top = 116, Width = 140, Height = 40,
            Text = "Transfer",
        };

        _homeBtn = new Button
        {
            Left = 10, Top = 200, Width = 100, Height = 36,
            Text = "Home",
        };
        _abortBtn = new Button
        {
            Left = 120, Top = 200, Width = 100, Height = 36,
            Text = "Abort",
        };
        _closeBtn = new Button
        {
            Left = 240, Top = 255, Width = 100, Height = 30,
            Text = "Close",
            DialogResult = DialogResult.OK,
        };
        CancelButton = _closeBtn;

        _transferBtn.Click += (_, _) =>
        {
            if (_srcCombo.SelectedItem is FaceItem src)
                SelectedSrc = src.Idx;
            if (_dstCombo.SelectedItem is FaceItem dst)
                SelectedDst = dst.Idx;
            SelectedAction = Action.Transfer;
            DialogResult = DialogResult.OK;
            Close();
        };

        _homeBtn.Click += (_, _) =>
        {
            SelectedAction = Action.Home;
            DialogResult = DialogResult.OK;
            Close();
        };

        _abortBtn.Click += (_, _) =>
        {
            SelectedAction = Action.Abort;
            DialogResult = DialogResult.OK;
            Close();
        };

        Controls.AddRange(new Control[]
        {
            _stateLabel, _faceLabel, _wafLabel, _errLabel,
            frLabel, _srcCombo, toLabel, _dstCombo, _transferBtn,
            _homeBtn, _abortBtn, _closeBtn,
        });

        DarkModeHelper.FixControls(this);
    }

    public void UpdateMirror(RobotState state, RobotPhase phase,
                             string faceName, bool hasWafer, RobotErrorCode errCode)
    {
        _stateLabel.Text = $"State:    {state}";
        _faceLabel.Text  = $"Face:     {faceName}";
        _wafLabel.Text   = $"HasWafer: {(hasWafer ? "yes" : "no")}";
        _errLabel.Text   = $"Error:    {(errCode == RobotErrorCode.Ok ? "-" : errCode.ToString())}";

        // Per spec: Home in any non-moving state (Startup is the boot default, so a fresh
        // robot can be homed); Abort while Homing or Transferring; Transfer only when Idle.
        _homeBtn.Enabled     = state is RobotState.Startup
                                       or RobotState.NotHomed
                                       or RobotState.Idle
                                       or RobotState.Error;
        _abortBtn.Enabled    = state is RobotState.Homing
                                       or RobotState.Transferring;
        _transferBtn.Enabled = state == RobotState.Idle;
    }

    private static Label MakeLabel(int x, int y, string text) =>
        new() { Left = x, Top = y, Width = 330, AutoSize = false, Text = text };

    /// <summary><see cref="DarkComboBox"/> owner-draws items via ToString and ignores DisplayMember.</summary>
    private readonly record struct FaceItem(string Name, byte Idx)
    {
        public override string ToString() => Name;
    }
}
