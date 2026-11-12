using System.Collections.Immutable;
using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

/// <summary>Region markers are always full-line <see cref="LineCommentTrivia"/> tokens;
/// user comments are left untouched.</summary>
public static class RegionTrivia
{
    public static Trivia PrependOpen(Trivia t, string name, bool collapsed)
        => new(t.Tokens.Insert(0, new LineCommentTrivia(RegionMarker.FormatOpen(name, collapsed))));

    /// <summary>Insert a #region open marker after any leading #endregion close markers.
    /// A close belongs to the region ending just before this block and must precede the
    /// new open in textual order (else the parser closes the new region immediately,
    /// producing a 0-step nested region). Equivalent to <see cref="PrependOpen"/> when
    /// there is no leading close.</summary>
    public static Trivia InsertOpen(Trivia t, string name, bool collapsed)
    {
        int at = 0;
        while (at < t.Tokens.Length
               && t.Tokens[at] is LineCommentTrivia c && RegionMarker.IsClose(c.Text))
            at++;
        return new Trivia(t.Tokens.Insert(at, new LineCommentTrivia(RegionMarker.FormatOpen(name, collapsed))));
    }

    public static Trivia PrependClose(Trivia t)
        => new(t.Tokens.Insert(0, new LineCommentTrivia(RegionMarker.FormatClose())));

    public static Trivia RemoveRegionMarkers(Trivia t)
    {
        var kept = t.Tokens.Where(tok =>
            tok is not LineCommentTrivia c ||
            (RegionMarker.TryParseOpen(c.Text) is null && !RegionMarker.IsClose(c.Text)));
        return new Trivia(kept.ToImmutableArray());
    }

    public static Trivia SetCollapsedOnOpen(Trivia t, bool collapsed)
    {
        var builder = t.Tokens.ToBuilder();
        for (int i = 0; i < builder.Count; i++)
        {
            if (builder[i] is LineCommentTrivia c && RegionMarker.TryParseOpen(c.Text) is { } open)
            {
                builder[i] = new LineCommentTrivia(RegionMarker.FormatOpen(open.Name, collapsed));
                break;
            }
        }
        return new Trivia(builder.ToImmutable());
    }

    public static Trivia RemoveFirstOpen(Trivia t, out bool wasCollapsed)
    {
        wasCollapsed = false;
        var builder = t.Tokens.ToBuilder();
        for (int i = 0; i < builder.Count; i++)
        {
            if (builder[i] is LineCommentTrivia c && RegionMarker.TryParseOpen(c.Text) is { } open)
            {
                wasCollapsed = open.Collapsed;
                builder.RemoveAt(i);
                break;
            }
        }
        return new Trivia(builder.ToImmutable());
    }

    public static RegionOpen? FirstOpen(Trivia t)
    {
        foreach (var tok in t.Tokens)
            if (tok is LineCommentTrivia c && RegionMarker.TryParseOpen(c.Text) is { } open)
                return open;
        return null;
    }

    public static bool HasClose(Trivia t)
        => t.Tokens.Any(tok => tok is LineCommentTrivia c && RegionMarker.IsClose(c.Text));
}
