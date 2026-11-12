using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Forms;

public class frmSplash : Form
{
    private readonly Label lblAppName;
    private readonly Label lblVersion;
    private readonly Label lblStatus;
    private readonly ProgressBar progressBar;

    public frmSplash()
    {
        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(DpiScale.Px(420), DpiScale.Px(260));
        bool dark = VacuumTheme.IsDark;
        Color bg          = dark ? VacuumTheme.PanelBackground : Color.White;
        Color titleFg     = dark ? VacuumTheme.TextPrimary     : Color.FromArgb(40, 40, 80);
        Color textFg      = dark ? VacuumTheme.TextPrimary     : Color.Gray;
        Color dimFg       = dark ? VacuumTheme.TextDim         : Color.DimGray;
        Color copyrightFg = dark ? VacuumTheme.TextDim         : Color.LightGray;
        Color borderFg    = dark ? VacuumTheme.PanelBorder     : Color.FromArgb(180, 180, 200);

        BackColor = bg;
        TopMost = true;
        ShowInTaskbar = false;

        lblAppName = new Label
        {
            Text = "APS System Control",
            Font = ScaledFont.Create(18f, FontStyle.Bold),
            ForeColor = titleFg,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(DpiScale.Px(396), DpiScale.Px(50)),
            Location = new Point(DpiScale.Px(12), DpiScale.Px(40))
        };

        string version = APS.SystemControl.Core.AppVersion.Full;
        lblVersion = new Label
        {
            Text = $"Version {version}",
            Font = ScaledFont.Create(10f),
            ForeColor = textFg,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(DpiScale.Px(396), DpiScale.Px(25)),
            Location = new Point(DpiScale.Px(12), DpiScale.Px(92))
        };

        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Style = ProgressBarStyle.Continuous,
            Size = new Size(DpiScale.Px(360), DpiScale.Px(22)),
            Location = new Point(DpiScale.Px(30), DpiScale.Px(145))
        };

        lblStatus = new Label
        {
            Text = "Initializing...",
            Font = ScaledFont.Create(9f),
            ForeColor = dimFg,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(DpiScale.Px(396), DpiScale.Px(22)),
            Location = new Point(DpiScale.Px(12), DpiScale.Px(175))
        };

        var lblCopyright = new Label
        {
            Text = $"(c) 2024-{DateTime.Now.Year} APS Technology",
            Font = ScaledFont.Create(8f),
            ForeColor = copyrightFg,
            TextAlign = ContentAlignment.MiddleCenter,
            AutoSize = false,
            Size = new Size(DpiScale.Px(396), DpiScale.Px(20)),
            Location = new Point(DpiScale.Px(12), DpiScale.Px(225))
        };

        Controls.Add(lblAppName);
        Controls.Add(lblVersion);
        Controls.Add(progressBar);
        Controls.Add(lblStatus);
        Controls.Add(lblCopyright);

        Paint += (_, e) =>
        {
            using var pen = new Pen(borderFg, 2);
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };

        DarkModeHelper.FixControls(this);
    }

    /// <summary>
    /// Thread-safe: can be called from the initialization thread.
    /// Auto-closes when progress reaches 100.
    /// </summary>
    /// <param name="progress">0-100.</param>
    public void UpdateStatus(string message, int progress)
    {
        if (IsDisposed) return;

        if (InvokeRequired)
        {
            try
            {
                Invoke(() => UpdateStatus(message, progress));
            }
            catch (ObjectDisposedException)
            {
                // Form was disposed between check and invoke
            }
            return;
        }

        lblStatus.Text = message;
        progressBar.Value = Math.Clamp(progress, 0, 100);

        if (progress >= 100)
        {
            // Brief delay so the user can see completion
            var closeTimer = new System.Windows.Forms.Timer { Interval = 500 };
            closeTimer.Tick += (_, _) =>
            {
                closeTimer.Stop();
                closeTimer.Dispose();
                Close();
            };
            closeTimer.Start();
        }
    }
}
