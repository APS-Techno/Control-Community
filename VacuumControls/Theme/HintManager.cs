using System.Windows.Forms;

namespace APS.Controls.Theme;

/// <summary>
/// Per-form hint manager. Owns one DarkToolTip, attaches a short hover tooltip to each
/// control, and — when a status-bar label is supplied and the hint carries a long part
/// ("short|long") — pushes the long text to the status bar on hover. Dispose with the form.
/// </summary>
public sealed class HintManager : IDisposable
{
    private readonly DarkToolTip _tip = new();
    private readonly ToolStripStatusLabel? _status;
    private readonly List<(Control C, EventHandler Enter, EventHandler Leave)> _wired = new();

    public HintManager(Control owner, ToolStripStatusLabel? statusTarget = null)
    {
        _ = owner;             // reserved; tooltips attach per-control via SetToolTip
        _status = statusTarget;
    }

    /// <summary>Attach a hint. <paramref name="hint"/> is "short" or "short|long".
    /// The long part is pushed to the status bar only when a statusTarget was provided.</summary>
    public void Set(Control control, string hint)
    {
        var (shortHint, longHint) = HintText.Parse(hint);
        _tip.SetToolTip(control, shortHint);

        if (longHint is { Length: > 0 } && _status != null)
        {
            string captured = longHint;
            EventHandler enter = (_, _) => _status.Text = captured;
            EventHandler leave = (_, _) => _status.Text = string.Empty;
            control.MouseEnter += enter;
            control.MouseLeave += leave;
            _wired.Add((control, enter, leave));
        }
    }

    public void Dispose()
    {
        foreach (var (c, enter, leave) in _wired)
        {
            c.MouseEnter -= enter;
            c.MouseLeave -= leave;
        }
        _wired.Clear();
        _tip.Dispose();
    }
}
