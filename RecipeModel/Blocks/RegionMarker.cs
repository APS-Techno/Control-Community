using System.Text;

namespace APS.RecipeModel.Blocks;

/// <summary>Single source of truth for parsing and formatting #region / #endregion markers,
/// shared by the block overlay, the script highlighter, and the validator.</summary>
public static class RegionMarker
{
    private const string CollapsedFlag = "[collapsed]";

    /// <summary><paramref name="commentText"/> still includes the leading '#' or '//'.</summary>
    public static RegionOpen? TryParseOpen(string commentText)
    {
        string body = StripPrefix(commentText);
        if (!StartsWithWord(body, "region", out string rest)) return null;
        rest = rest.Trim();
        bool collapsed = false;
        if (rest.EndsWith(CollapsedFlag, StringComparison.OrdinalIgnoreCase))
        {
            collapsed = true;
            rest = rest[..^CollapsedFlag.Length].TrimEnd();
        }
        return new RegionOpen(rest, collapsed);
    }

    public static bool IsClose(string commentText)
        => StartsWithWord(StripPrefix(commentText), "endregion", out _);

    public static string FormatOpen(string name, bool collapsed)
    {
        var sb = new StringBuilder("#region");
        if (!string.IsNullOrWhiteSpace(name)) sb.Append(' ').Append(name.Trim());
        if (collapsed) sb.Append(' ').Append(CollapsedFlag);
        return sb.ToString();
    }

    public static string FormatClose() => "#endregion";

    private static string StripPrefix(string commentText)
    {
        string t = commentText.TrimStart();
        if (t.StartsWith("//")) t = t[2..];
        else if (t.StartsWith("#")) t = t[1..];
        return t.TrimStart();
    }

    private static bool StartsWithWord(string body, string keyword, out string rest)
    {
        rest = "";
        if (!body.StartsWith(keyword, StringComparison.OrdinalIgnoreCase)) return false;
        if (body.Length > keyword.Length && !char.IsWhiteSpace(body[keyword.Length])) return false;
        rest = body.Length > keyword.Length ? body[keyword.Length..] : "";
        return true;
    }
}

public readonly record struct RegionOpen(string Name, bool Collapsed);
