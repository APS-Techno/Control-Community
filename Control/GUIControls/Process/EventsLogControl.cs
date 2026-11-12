using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using APS.SystemControl.Core.Logging;

namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Styled after the Delphi DepositionControl2 main-screen Memo.
///
/// Deliberately does NOT call
/// DarkModeHelper.FixControls / SetWindowTheme("DarkMode_Explorer"): that
/// applies the Explorer-themed (beige, thin) scrollbar chrome which we don't
/// want here. Instead we just set BackColor/ForeColor manually and let the
/// OS-default Win11 modern dark scrollbar (blue accent thumb) render through.
/// </summary>
[DesignerCategory("Code")]
public class EventsLogControl : BufferedRichTextBox
{
    private const int MaxLines = 2000;
    private const int TrimAtOverflow = 200;

    // Recipe-lifecycle accent colors. Picked to read on BgDark (#0D1B2A)
    // and to match existing palette precedent: Accent (#48A9FE), LimeGreen,
    // amber-Processing (#FFB74D), alarm-red (#FF5555).
    private static readonly Color StartedColor  = Color.FromArgb(72, 169, 254);
    private static readonly Color CompleteColor = Color.LimeGreen;
    private static readonly Color FailedColor   = Color.FromArgb(255, 85, 85);
    private static readonly Color AbortedColor  = Color.FromArgb(255, 183, 77);
    private static readonly Color TimestampColor = Color.FromArgb(119, 136, 153); // TextDim

    public EventsLogControl()
    {
        Font = new Font("Consolas", 9f);
        ReadOnly = true;
        HideSelection = true;
        TabStop = false;

        if (APS.Controls.Theme.VacuumTheme.IsDark)
        {
            // Industrial dark palette — mirrors the values DarkModeHelper
            // would set in its RichTextBox case (BgDark / TextPrimary).
            BackColor = Color.FromArgb(13, 27, 42);
            ForeColor = Color.FromArgb(200, 214, 229);
        }

        EventLogBus.Entry += OnBusEntry;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>
    /// Set DWMWA_USE_IMMERSIVE_DARK_MODE on the RichEdit's own HWND so the
    /// Win11 modern dark scrollbar (blue accent, wider thumb) renders. The
    /// flag on FrmMain doesn't propagate through the deeply nested host
    /// (FrmMain -> tab -> vacuumSchematic -> FloatingPanel -> ContentPanel -> this),
    /// so apply it directly. Deliberately do NOT call SetWindowTheme — that
    /// would override the modern style with the gray/beige Explorer chrome.
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (APS.Controls.Theme.VacuumTheme.IsDark)
        {
            int dark = 1;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }

        // Seed line so the panel isn't blank during the ~2 s boot-gate
        // window before EventLogBus.MarkBootComplete fires "System is ready".
        if (TextLength == 0)
            AppendColored(DateTime.Now, "System is starting...", Color.Empty);
    }

    /// <summary>
    /// When true, render every level (not just Operator). Not wired to UI yet.
    /// </summary>
    public bool ShowAllLevels { get; set; }

    public void AppendEvent(EventLogEntry entry)
    {
        if (!ShowAllLevels && entry.Level != LogEventLevel.Operator) return;
        if (IsDisposed) return;
        Color msgColor = ColorFor(entry.Message);
        // Same marshal-target pattern as BaseProcessPanel's log drawer: never
        // create the handle on a publisher thread, and re-check disposal on the
        // UI thread.
        if (IsHandleCreated && !InvokeRequired)
        {
            AppendColored(entry.Timestamp, entry.Message, msgColor);
            return;
        }
        var marshalTarget = IsHandleCreated ? (Control)this : FindForm();
        if (marshalTarget is null || !marshalTarget.IsHandleCreated) return;
        try
        {
            marshalTarget.BeginInvoke(() =>
            {
                if (!IsDisposed) AppendColored(entry.Timestamp, entry.Message, msgColor);
            });
        }
        catch (InvalidOperationException) { /* disposed between check and post */ }
    }

    private static Color ColorFor(string message) => message switch
    {
        _ when message.StartsWith("Recipe started:",  StringComparison.Ordinal) => StartedColor,
        _ when message.StartsWith("Recipe complete:", StringComparison.Ordinal) => CompleteColor,
        _ when message.StartsWith("Recipe failed:",   StringComparison.Ordinal) => FailedColor,
        _ when message.StartsWith("Recipe aborted:",  StringComparison.Ordinal) => AbortedColor,
        _ => Color.Empty, // sentinel: use default ForeColor
    };

    /// <summary>
    /// Date of the most recently emitted day-separator. Control runs 24/7, so a
    /// bare <c>HH:mm:ss</c> stamp is ambiguous across midnight; instead of
    /// repeating the date on every line we emit a single separator row whenever
    /// the day rolls over (or on the first line).
    /// </summary>
    private DateTime _lastSeparatorDate = DateTime.MinValue;

    private void MaybeAppendDateSeparator(DateTime when)
    {
        if (when.Date == _lastSeparatorDate.Date) return;
        _lastSeparatorDate = when.Date;
        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionColor = TimestampColor;
        AppendText(
            "------ " + when.ToString("yyyy-MM-dd  dddd", CultureInfo.InvariantCulture) + " ------"
            + Environment.NewLine);
        SelectionColor = ForeColor;
    }

    private void AppendColored(DateTime when, string message, Color msgColor)
    {
        MaybeAppendDateSeparator(when);

        SelectionStart = TextLength;
        SelectionLength = 0;
        SelectionColor = TimestampColor;
        AppendText(when.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  ");
        SelectionColor = msgColor == Color.Empty ? ForeColor : msgColor;
        AppendText(message + Environment.NewLine);
        SelectionColor = ForeColor;

        if (Lines.Length > MaxLines)
        {
            int trimEnd = GetFirstCharIndexFromLine(TrimAtOverflow);
            if (trimEnd > 0)
            {
                SelectionStart = 0;
                SelectionLength = trimEnd;
                SelectedText = "";
            }
        }
        SelectionStart = TextLength;
        ScrollToCaret();
    }

    private void OnBusEntry(EventLogEntry entry) => AppendEvent(entry);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            EventLogBus.Entry -= OnBusEntry;
        base.Dispose(disposing);
    }
}
