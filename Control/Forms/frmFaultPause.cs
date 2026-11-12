using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;
using APS.SystemControl.Core;

namespace APS.SystemControl.Forms;

/// <summary>
/// Modeless fault-pause dialog: the failed line, the driver's reason, a countdown to the
/// watchdog, and Retry / Skip / Abort / Hold. Modeless (Show, not ShowDialog) so live updates
/// keep flowing; the engine's decision TCS is what actually pauses the recipe. Only one instance
/// exists at a time; the engine closes it via "FaultPauseEnd" whichever way the pause ends.
/// <para>Hold is the way OUT of this dialog rather than out of the pause: it parks the run,
/// disarms the watchdog and unlocks manual hardware control, and the dialog closes and STAYS
/// closed (the held wait deliberately does not re-post the pause). From there the operator's
/// only affordances are the toolbar's Resume and Abort.</para>
/// </summary>
public class frmFaultPause : Form
{
    private static frmFaultPause? _current;

    private readonly Label lblStep, lblReason, lblWarning, lblCountdown, lblHoldDone, lblHoldProblems;
    private readonly Button btnRetry, btnSkip, btnAbort, btnHold;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    /// <summary>When the watchdog will abort, or null for a wait that cannot time out (a held
    /// pause). Nullable rather than a sentinel: see <see cref="UpdateCountdown"/>.</summary>
    private readonly DateTime? _deadlineUtc;
    private readonly Action<FaultDecision> _onDecision;
    // The rows that actually show, in order — the layout's only source of truth. It cannot ask
    // Control.Visible: that getter folds in the parent's own visibility, and the form isn't
    // shown yet while LayoutControls() runs from the constructor, so every child reads false
    // regardless of what was set.
    private readonly List<Label> _rows = [];
    private bool _decided;

    internal frmFaultPause(FaultPauseInfo info, Action<FaultDecision> onDecision)
    {
        _deadlineUtc = info.DeadlineUtc;
        _onDecision = onDecision;

        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = "Recipe paused on a fault";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false; MaximizeBox = false; ShowInTaskbar = false;
        // No [X]: closing without deciding leaves the run paused — gas and plasma as they were —
        // with nothing on screen but the amber pill until the watchdog fires. The FormClosing
        // guard below covers Alt+F4, which ControlBox alone does not.
        ControlBox = false;
        TopMost = true;
        // 700: the row of four buttons needs this client width.
        ClientSize = new Size(DpiScale.Px(700), DpiScale.Px(300));
        Padding = new Padding(DpiScale.Px(20));

        int w = DpiScale.Px(660);   // label wrap width
        Label Make(string text, float size, bool bold = false) => new()
        {
            AutoSize = true,
            MaximumSize = new Size(w, 0),
            Font = ScaledFont.Create(size, bold ? FontStyle.Bold : FontStyle.Regular),
            Text = text,
        };

        lblStep = Make($"Step failed:  {info.Step}", 11f, bold: true);
        lblReason = Make(info.Reason, 10f);

        // Report what the hold DID, never what it was supposed to do: FAILURE_HOLD is arbitrary
        // profile script (it may contain `mfc Ar 0` or `power X off`), so the bullets say what
        // ran; anything not listed did not happen, which is the only claim this dialog is
        // entitled to make.
        static string Bullets(IReadOnlyList<string> items) =>
            string.Join("\n", items.Select(i => "   • " + i));
        lblHoldDone = Make(info.Hold.Done.Count > 0
            ? "Machine hold — what actually happened:\n" + Bullets(info.Hold.Done)
            : "Machine hold: no action was performed.", 9f);
        lblHoldProblems = Make(info.Hold.Problems.Count > 0
            ? "HOLD INCOMPLETE — the machine is NOT fully held:\n" + Bullets(info.Hold.Problems)
            : "", 9f, bold: true);
        lblHoldProblems.ForeColor = Color.FromArgb(255, 90, 90);

        lblWarning = Make(
            "Skipping a power / gas / valve step: later recipe lines assume it succeeded.", 9f, bold: true);
        lblWarning.ForeColor = Color.FromArgb(255, 90, 90);
        lblCountdown = Make("", 10f);

        _rows.Add(lblStep);
        _rows.Add(lblReason);
        _rows.Add(lblHoldDone);
        if (info.Hold.Problems.Count > 0) _rows.Add(lblHoldProblems);
        if (CoreThread.IsEnergyStep(info.Step.Split(' '))) _rows.Add(lblWarning);
        _rows.Add(lblCountdown);
        foreach (var l in new[] { lblHoldProblems, lblWarning })
            l.Visible = _rows.Contains(l);

        Button MakeButton(string text, FaultDecision d)
        {
            var b = new Button { Text = text, Size = new Size(DpiScale.Px(110), DpiScale.Px(34)) };
            b.Click += (_, _) => Decide(d);
            return b;
        }
        btnRetry = MakeButton("Retry", FaultDecision.Retry);
        btnSkip = MakeButton("Skip", FaultDecision.Skip);
        btnAbort = MakeButton("Abort", FaultDecision.Abort);
        btnHold = MakeButton("Hold — fix it myself", FaultDecision.Hold);
        btnHold.Width = DpiScale.Px(170);
        AcceptButton = btnRetry;

        Controls.AddRange([lblStep, lblReason, lblHoldDone, lblHoldProblems, lblWarning, lblCountdown,
            btnRetry, btnSkip, btnAbort, btnHold]);
        LayoutControls();
        UpdateCountdown();
        _tick.Tick += (_, _) => UpdateCountdown();
        _tick.Start();

        // Only a decision, or the engine, may close this dialog. CloseReason.UserClosing is the
        // title-bar X and Alt+F4; a programmatic Close() (Decide, and the engine's Dismiss when
        // the pause ends for another reason) arrives as CloseReason.None, and an app exit as
        // ApplicationExitCall / WindowsShutDown — none of those are blocked.
        FormClosing += (_, e) =>
        {
            if (!_decided && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        };
        FormClosed += (_, _) => { _tick.Stop(); _tick.Dispose(); if (_current == this) _current = null; };
        DarkModeHelper.FixControls(this);
    }

    private void LayoutControls()
    {
        int x = DpiScale.Px(20), y = DpiScale.Px(20), gap = DpiScale.Px(10);
        foreach (var l in _rows)
        {
            l.Location = new Point(x, y);
            l.PerformLayout();
            y = l.Bottom + gap;
        }
        int by = y + gap;
        int bw = btnRetry.Width, bgap = DpiScale.Px(12);
        // btnHold is wider than the other three, so the run's total width is not 4 * bw.
        int total = 3 * bw + btnHold.Width + 3 * bgap;
        int bx = (ClientSize.Width - total) / 2;
        btnRetry.Location = new Point(bx, by);
        btnSkip.Location = new Point(bx + bw + bgap, by);
        btnAbort.Location = new Point(bx + 2 * (bw + bgap), by);
        btnHold.Location = new Point(bx + 3 * (bw + bgap), by);
        ClientSize = new Size(ClientSize.Width, by + btnRetry.Height + DpiScale.Px(20));
    }

    private void UpdateCountdown()
    {
        // A held pause carries no deadline (the held wait does not re-post the pause — Ruling C);
        // never print a countdown for a wait that cannot time out. The test is the ABSENCE of a
        // deadline, not a sentinel value: a held pause reusing `started + timeout` with
        // Timeout.InfiniteTimeSpan (-1 ms) would land one millisecond in the PAST and print
        // "Watchdog expired — aborting." on a wait that can never time out.
        if (_deadlineUtc is not { } deadline)
        {
            lblCountdown.Text = "Held — the run waits for Resume or Abort. No timeout.";
            return;
        }
        var left = deadline - DateTime.UtcNow;
        if (left <= TimeSpan.Zero) { lblCountdown.Text = "Watchdog expired — aborting."; return; }
        // "m\:ss" silently DROPS the hour component, so a 3600 s watchdog counted down from
        // "0:00". The clamp allows up to 86 400 s, so hours are reachable by configuration.
        string remaining = left.TotalHours >= 1 ? left.ToString(@"h\:mm\:ss") : left.ToString(@"m\:ss");
        lblCountdown.Text = $"Aborts automatically in {remaining} unless you decide.";
    }

    private void Decide(FaultDecision d)
    {
        if (_decided) return;
        _decided = true;
        _onDecision(d);
        Close();
    }

    /// <summary>Shows the dialog (replacing any stale one). Call on the UI thread.</summary>
    public static void Present(IWin32Window owner, FaultPauseInfo info, Action<FaultDecision> onDecision)
    {
        Dismiss();
        _current = new frmFaultPause(info, onDecision);
        _current.Show(owner);
        _current.Activate();
    }

    /// <summary>Closes the open dialog, if any, without deciding. Call on the UI thread.</summary>
    public static void Dismiss()
    {
        var f = _current;
        _current = null;
        if (f is { IsDisposed: false }) f.Close();
    }
}
