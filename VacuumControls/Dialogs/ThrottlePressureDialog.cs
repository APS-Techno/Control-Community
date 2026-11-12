using APS.Controls.Theme;

namespace APS.Controls.Dialogs;

public enum ThrottleAction { Cancel, Set, Open, Close, Stop }

public class ThrottlePressureDialog : Form
{
    private readonly TextBox _txtPressure;
    private readonly Label _lblPrompt;
    private readonly Label _lblError;

    public ThrottleAction Action { get; private set; } = ThrottleAction.Cancel;
    public float? PressureTorr { get; private set; }

    public ThrottlePressureDialog(string deviceId, float currentPositionPct)
    {
        Text = $"Throttle {deviceId}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        // Layout values are 96-DPI logical pixels; AutoScaleMode.None: manual
        // scaling only, no WinForms font-autoscale on top (see DpiScale).
        AutoScaleMode = AutoScaleMode.None;
        // Wide enough for the full AutoSize prompt ("Target pressure … Current
        // position: xxx.x%") which otherwise clips on the right.
        ClientSize = new Size(DpiScale.Px(400), DpiScale.Px(150));

        _lblPrompt = new Label
        {
            AutoSize = true,
            Location = new Point(DpiScale.Px(12), DpiScale.Px(9)),
            Text = $"Target pressure (e.g. 5mT, 0.005, 1uT). Current position: {currentPositionPct:F1}%",
        };
        _txtPressure = new TextBox { Location = new Point(DpiScale.Px(15), DpiScale.Px(32)), Size = new Size(DpiScale.Px(370), DpiScale.Px(23)) };
        _lblError = new Label
        {
            AutoSize = true,
            Location = new Point(DpiScale.Px(15), DpiScale.Px(58)),
            ForeColor = Color.Red,
            Text = "",
        };

        var btnSet = NewButton("Set",     12, 90, _ => OnSet());
        var btnOpen = NewButton("Open",   90, 90, _ => Finish(ThrottleAction.Open));
        var btnClose = NewButton("Close", 168, 90, _ => Finish(ThrottleAction.Close));
        var btnStop = NewButton("Stop",   246, 90, _ => Finish(ThrottleAction.Stop));
        var btnCancel = NewButton("Cancel", 324, 90, _ => Finish(ThrottleAction.Cancel));

        AcceptButton = btnSet;
        CancelButton = btnCancel;

        Controls.AddRange(new Control[] { _lblPrompt, _txtPressure, _lblError,
            btnSet, btnOpen, btnClose, btnStop, btnCancel });

        DarkModeHelper.FixControls(this);
    }

    private static Button NewButton(string text, int x, int y, Action<Button> onClick)
    {
        var b = new Button
        {
            Text = text,
            Location = new Point(DpiScale.Px(x), DpiScale.Px(y)),
            Size = new Size(DpiScale.Px(60), DpiScale.Px(28)),
        };
        b.Click += (_, _) => onClick(b);
        return b;
    }

    private void OnSet()
    {
        if (TryParsePressure(_txtPressure.Text, out float torr))
        {
            PressureTorr = torr;
            Action = ThrottleAction.Set;
            DialogResult = DialogResult.OK;
            Close();
        }
        else
        {
            _lblError.Text = "Enter a positive pressure with optional unit (T, mT, uT, nT, kT).";
        }
    }

    private void Finish(ThrottleAction action)
    {
        Action = action;
        DialogResult = action == ThrottleAction.Cancel ? DialogResult.Cancel : DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Parses a pressure string with optional unit suffix (T, mT, uT, nT, kT)
    /// into Torr. Bare numbers are treated as Torr (matches recipe DSL).
    /// </summary>
    public static bool TryParsePressure(string input, out float torr)
    {
        torr = 0f;
        if (string.IsNullOrWhiteSpace(input)) return false;

        string s = input.Trim();
        float multiplier = 1f;

        if (s.EndsWith("kT", StringComparison.Ordinal)) { multiplier = 1e3f; s = s[..^2]; }
        else if (s.EndsWith("mT", StringComparison.Ordinal)) { multiplier = 1e-3f; s = s[..^2]; }
        else if (s.EndsWith("uT", StringComparison.Ordinal)) { multiplier = 1e-6f; s = s[..^2]; }
        else if (s.EndsWith("nT", StringComparison.Ordinal)) { multiplier = 1e-9f; s = s[..^2]; }
        else if (s.EndsWith("T",  StringComparison.Ordinal)) { multiplier = 1f;    s = s[..^1]; }
        else if (s.Any(char.IsLetter)) return false;

        if (!float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float value))
            return false;
        if (value < 0f || float.IsNaN(value) || float.IsInfinity(value)) return false;

        torr = value * multiplier;
        return true;
    }
}
