namespace APS.Controls.Theme;

/// <summary>
/// Implemented by controls that prefer to manage their own scroll offset
/// (rather than relying on WinForms AutoScroll / Win32 WM_VSCROLL). When
/// the control hosted by <see cref="DarkScrollHost"/> implements this
/// interface, the host drives scrolling through it directly.
/// </summary>
public interface ICustomScrollSource
{
    Size ContentSize { get; }
    Size ViewportSize { get; }
    Point ScrollPosition { get; set; }
    event EventHandler? ScrollMetricsChanged;
}
