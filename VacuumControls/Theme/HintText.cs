namespace APS.Controls.Theme;

/// <summary>
/// Parses a Delphi-style hint string: "short" or "short|long". The short part is the
/// hover tooltip; the optional long part is for the status bar. Split is on the FIRST '|'
/// so the long part may itself contain '|'.
/// </summary>
public static class HintText
{
    public static (string Short, string? Long) Parse(string? text)
    {
        if (string.IsNullOrEmpty(text)) return ("", null);
        int i = text.IndexOf('|');
        if (i < 0) return (text.Trim(), null);
        string s = text[..i].Trim();
        string l = text[(i + 1)..].Trim();
        return (s, l.Length == 0 ? null : l);
    }
}
