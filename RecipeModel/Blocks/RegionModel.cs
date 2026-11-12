using APS.RecipeModel.Ast;

namespace APS.RecipeModel.Blocks;

public sealed record RegionSpan(
    string Name, bool Collapsed, int StartIndex, int EndIndex, IReadOnlyList<RegionSpan> Children)
{
    public int StepCount => EndIndex - StartIndex + 1;
}

public sealed record RegionModel(IReadOnlyList<RegionSpan> Roots, IReadOnlyList<string> Warnings)
{
    public static readonly RegionModel Empty =
        new(Array.Empty<RegionSpan>(), Array.Empty<string>());

    private sealed class Frame(string name, bool collapsed, int start)
    {
        public string Name = name;
        public bool Collapsed = collapsed;
        public int Start = start;
        public List<RegionSpan> Children = new();
    }

    /// <summary>Reads the blocks' leading trivia and the last block's trailing trivia.</summary>
    public static RegionModel Build(IReadOnlyList<BlockBase> blocks)
    {
        var open = new Stack<Frame>();
        var roots = new List<RegionSpan>();
        var warnings = new List<string>();

        void CloseAt(int endIndex)
        {
            if (open.Count == 0) { warnings.Add("stray #endregion"); return; }
            var f = open.Pop();
            var span = new RegionSpan(f.Name, f.Collapsed, f.Start, endIndex, f.Children);
            if (open.Count == 0) roots.Add(span);
            else open.Peek().Children.Add(span);
        }

        for (int i = 0; i < blocks.Count; i++)
        {
            foreach (var tok in blocks[i].LeadingTrivia.Tokens)
            {
                if (tok is not LineCommentTrivia c) continue;
                if (RegionMarker.IsClose(c.Text)) CloseAt(i - 1);
                else if (RegionMarker.TryParseOpen(c.Text) is { } o)
                    open.Push(new Frame(o.Name, o.Collapsed, i));
            }
        }

        if (blocks.Count > 0)
            foreach (var tok in blocks[^1].TrailingTrivia.Tokens)
                if (tok is LineCommentTrivia c && RegionMarker.IsClose(c.Text))
                    CloseAt(blocks.Count - 1);

        while (open.Count > 0)
        {
            warnings.Add($"unclosed region '{open.Peek().Name}'");
            CloseAt(blocks.Count - 1);
        }

        return new RegionModel(roots, warnings);
    }
}
