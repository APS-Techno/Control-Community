using System.ComponentModel;
using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Controls.General;

[ToolboxItem(false)]
public class AlarmBannerPanel : Panel
{
    private readonly List<string> _alarms = [];
    private readonly Dictionary<string, (string Message, DateTime LastSeen)> _suppressed = new();
    private readonly System.Windows.Forms.Timer _suppressedSweep = new() { Interval = 1000 };
    private readonly Label _messageLabel;
    private readonly Button _silenceButton;
    private readonly Button _resetButton;
    private readonly PictureBox _icon;
    private readonly System.Windows.Forms.Timer _beepTimer;
    private bool _silenced;

    public event Action? AlarmsReset;

    public AlarmBannerPanel()
    {
        Dock = DockStyle.Bottom;
        Height = DpiScale.Px(40);
        Visible = false;
        BackColor = Color.FromArgb(139, 0, 0);

        _icon = new PictureBox
        {
            Image = Properties.Resources.alarm_icon_27,
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(DpiScale.Px(30), DpiScale.Px(30)),
            Location = new Point(DpiScale.Px(5), DpiScale.Px(5)),
            BackColor = Color.Transparent
        };

        _messageLabel = new Label
        {
            ForeColor = Color.White,
            Font = ScaledFont.Create(10f, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.Transparent,
            Location = new Point(DpiScale.Px(40), 0),
            Height = DpiScale.Px(40),
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
        };

        _resetButton = new Button
        {
            Text = "Reset",
            ForeColor = Color.White,
            BackColor = Color.FromArgb(180, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(DpiScale.Px(75), DpiScale.Px(28)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand
        };
        _resetButton.FlatAppearance.BorderColor = Color.FromArgb(220, 60, 60);
        _resetButton.Click += (_, _) => ResetAlarms();

        _silenceButton = new Button
        {
            Text = "Silence",
            ForeColor = Color.White,
            BackColor = Color.FromArgb(180, 0, 0),
            FlatStyle = FlatStyle.Flat,
            Size = new Size(DpiScale.Px(75), DpiScale.Px(28)),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand
        };
        _silenceButton.FlatAppearance.BorderColor = Color.FromArgb(220, 60, 60);
        _silenceButton.Click += (_, _) => ToggleSilence();

        _beepTimer = new System.Windows.Forms.Timer { Interval = 5000 };
        _beepTimer.Tick += (_, _) => System.Media.SystemSounds.Exclamation.Play();

        _suppressedSweep.Tick += (_, _) => SweepSuppressed();

        Controls.Add(_icon);
        Controls.Add(_messageLabel);
        Controls.Add(_silenceButton);
        Controls.Add(_resetButton);

        Resize += (_, _) => LayoutControls();
        LayoutControls();
    }

    private void LayoutControls()
    {
        if (Width <= 0) return;
        int pad = DpiScale.Px(6);
        _resetButton.Location = new Point(Width - _resetButton.Width - pad, (Height - _resetButton.Height) / 2);
        _silenceButton.Location = new Point(_resetButton.Left - _silenceButton.Width - pad, (Height - _silenceButton.Height) / 2);
        _messageLabel.Width = _silenceButton.Left - _messageLabel.Left - pad;
    }

    /// <summary>
    /// No-op if the message already exists.
    /// Called on UI thread (marshaled by FrmMain.DisplayAlarm).
    /// </summary>
    public void AddAlarm(string message)
    {
        if (_alarms.Contains(message))
            return;

        _alarms.Add(message);
        UpdateLabel();
        Visible = true;

        if (!_silenced && !_beepTimer.Enabled)
        {
            System.Media.SystemSounds.Exclamation.Play();
            _beepTimer.Start();
        }
    }

    /// <summary>
    /// Suppressed lines are NOT cleared — they auto-expire on 2s stale timeout.
    /// Ack'ing a suppressed alarm would be nonsensical (the user already chose
    /// to disable its rule).
    /// </summary>
    public void ResetAlarms()
    {
        _alarms.Clear();
        _beepTimer.Stop();
        _silenced = false;
        _silenceButton.Text = "Silence";
        if (_suppressed.Count == 0) Visible = false;
        UpdateLabel();
        AlarmsReset?.Invoke();
    }

    /// <summary>
    /// The caller invokes this every poll tick that an overridden rule's
    /// condition fires; the banner auto-removes the line 2 seconds after the
    /// last refresh so it disappears when the condition clears or the override
    /// is lifted. Does not trigger sound and cannot be cleared via Reset.
    /// </summary>
    public void RefreshSuppressedAlarm(string id, string message)
    {
        _suppressed[id] = (message, DateTime.UtcNow);
        if (!_suppressedSweep.Enabled) _suppressedSweep.Start();
        UpdateLabel();
        Visible = true;
    }

    private void SweepSuppressed()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-2);
        var stale = _suppressed.Where(kv => kv.Value.LastSeen < cutoff).Select(kv => kv.Key).ToList();
        foreach (var id in stale) _suppressed.Remove(id);
        if (_suppressed.Count == 0 && _alarms.Count == 0)
        {
            Visible = false;
            _suppressedSweep.Stop();
        }
        else
        {
            UpdateLabel();
        }
    }

    private void ToggleSilence()
    {
        _silenced = !_silenced;
        if (_silenced)
        {
            _beepTimer.Stop();
            _silenceButton.Text = "Silenced";
        }
        else
        {
            if (_alarms.Count > 0)
                _beepTimer.Start();
            _silenceButton.Text = "Silence";
        }
    }

    private void UpdateLabel()
    {
        var live = _alarms;
        var suppressed = _suppressed.Values.Select(v => "(suppressed) " + v.Message).ToList();

        if (live.Count == 0 && suppressed.Count == 0)
        {
            _messageLabel.Text = "";
            return;
        }

        var parts = new List<string>(live.Count + suppressed.Count);
        parts.AddRange(live);
        parts.AddRange(suppressed);

        _messageLabel.Text = parts.Count == 1 ? parts[0] : $"{parts.Count} active: {parts[^1]}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _beepTimer.Stop();
            _beepTimer.Dispose();
            _suppressedSweep.Stop();
            _suppressedSweep.Dispose();
        }
        base.Dispose(disposing);
    }
}
