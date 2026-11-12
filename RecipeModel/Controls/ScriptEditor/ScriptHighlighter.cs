using System.Text.RegularExpressions;

namespace APS.RecipeModel.Controls.ScriptEditor;

/// <summary>
/// Highlight passes run in order; the last pass that matches a span wins.
/// </summary>
public static class ScriptHighlighter
{
    private static readonly string[] Keywords =
    [
        "valve", "mfc", "pump", "stage", "delay", "loop", "endloop", "isolate",
        "var", "function", "endfunction", "bias", "biasArb", "set", "warmup",
        "pwait", "swait", "check", "holder", "shutter", "scan",
        "scanR", "deposit", "rotation", "rotate", "message", "wait", "begin", "end",
        "if", "else", "endif", "label", "goto", "start", "finish",
        "power", "ramp", "call", "transfer", "home", "abort", "state"
    ];

    private static readonly Color KeywordColor = Color.FromArgb(86, 156, 214);
    private static readonly Color NumberColor = Color.FromArgb(181, 137, 0);
    private static readonly Color VariableColor = Color.FromArgb(190, 132, 255);
    private static readonly Color StringColor = Color.FromArgb(206, 145, 120);
    private static readonly Color CommentColor = Color.FromArgb(106, 153, 85);
    private static readonly Color RegionColor = Color.FromArgb(120, 170, 220);

    private static readonly Regex NumberRegex = new(
        @"(?<!\w)[-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?(?!\w)",
        RegexOptions.Compiled);

    private static readonly Regex VariableRegex = new(
        @"\$\w+",
        RegexOptions.Compiled);

    private static readonly Regex StringRegex = new(
        @"""(?:[^""\\]|\\.)*""",
        RegexOptions.Compiled);

    private static readonly Regex CommentRegex = new(
        @"(?:#|//).*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static readonly Regex RegionLineRegex = new(
        @"^\s*(?:#|//)\s*(?:region\b.*|endregion\b.*)$",
        RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.IgnoreCase);

    private static readonly Regex KeywordRegex;

    static ScriptHighlighter()
    {
        string pattern = @"\b(" + string.Join("|", Keywords) + @")\b";
        KeywordRegex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // Cached font variants to avoid GDI object leaks
    private static Font? _fontRegular;
    private static Font? _fontBold;
    private static Font? _fontItalic;
    private static Font? _cachedBaseFont;

    private static void EnsureFonts(Font baseFont)
    {
        if (_cachedBaseFont != null && _cachedBaseFont.Name == baseFont.Name
            && _cachedBaseFont.Size == baseFont.Size) return;

        _fontRegular?.Dispose();
        _fontBold?.Dispose();
        _fontItalic?.Dispose();
        _cachedBaseFont = baseFont;
        _fontRegular = new Font(baseFont, FontStyle.Regular);
        _fontBold = new Font(baseFont, FontStyle.Bold);
        _fontItalic = new Font(baseFont, FontStyle.Italic);
    }

    /// <summary>Preserves scroll position and caret location.</summary>
    public static void Apply(RichTextBox box)
    {
        int selStart = box.SelectionStart;
        int selLen = box.SelectionLength;
        var scrollPos = GetScrollPos(box);

        EnsureFonts(box.Font);
        if (_fontRegular == null || _fontBold == null || _fontItalic == null) return;

        box.SuspendLayout();

        box.SelectAll();
        box.SelectionColor = box.ForeColor;
        box.SelectionFont = _fontRegular;

        string text = box.Text;

        foreach (Match m in KeywordRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = KeywordColor;
            box.SelectionFont = _fontBold;
        }

        foreach (Match m in NumberRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = NumberColor;
            box.SelectionFont = _fontRegular;
        }

        foreach (Match m in VariableRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = VariableColor;
            box.SelectionFont = _fontRegular;
        }

        foreach (Match m in StringRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = StringColor;
            box.SelectionFont = _fontRegular;
        }

        foreach (Match m in CommentRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = CommentColor;
            box.SelectionFont = _fontItalic;
        }

        foreach (Match m in RegionLineRegex.Matches(text))
        {
            box.Select(m.Index, m.Length);
            box.SelectionColor = RegionColor;
            box.SelectionFont = _fontBold;
        }

        box.Select(selStart, selLen);
        SetScrollPos(box, scrollPos);
        box.ResumeLayout();
    }

    private const int WM_USER = 0x0400;
    private const int EM_GETSCROLLPOS = WM_USER + 221;
    private const int EM_SETSCROLLPOS = WM_USER + 222;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref Point lParam);

    private static Point GetScrollPos(RichTextBox box)
    {
        var pt = new Point();
        SendMessage(box.Handle, EM_GETSCROLLPOS, IntPtr.Zero, ref pt);
        return pt;
    }

    private static void SetScrollPos(RichTextBox box, Point pos)
    {
        SendMessage(box.Handle, EM_SETSCROLLPOS, IntPtr.Zero, ref pos);
    }
}
