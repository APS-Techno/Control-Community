namespace APS.RecipeModel.Blocks;

public static class RegionCommands
{
    public static void WrapInRegion(BlockStackControl stack, IReadOnlyList<BlockBase> run, string name)
    {
        if (run.Count == 0) return;
        var blocks = (IList<BlockBase>)stack.Blocks;
        int start = blocks.IndexOf(run[0]);
        int end = blocks.IndexOf(run[^1]);
        if (start < 0 || end < 0) return;
        if (start > end) (start, end) = (end, start);

        var first = blocks[start];
        // InsertOpen (not PrependOpen): if this block already closes a preceding adjacent
        // region, the #endregion must stay ahead of the new #region open.
        first.SetLeadingTrivia(RegionTrivia.InsertOpen(first.LeadingTrivia, name, collapsed: false));

        if (end + 1 < blocks.Count)
        {
            var after = blocks[end + 1];
            after.SetLeadingTrivia(RegionTrivia.PrependClose(after.LeadingTrivia));
        }
        else
        {
            var last = blocks[end];
            last.SetTrailingTrivia(RegionTrivia.PrependClose(last.TrailingTrivia));
        }

        stack.RebuildRegionDecorations();
        first.RaisePublicFieldChanged();
    }

    public static void RemoveRegion(BlockStackControl stack, RegionSpan span)
    {
        var blocks = (IList<BlockBase>)stack.Blocks;
        if (span.StartIndex < 0 || span.StartIndex >= blocks.Count) return;

        var first = blocks[span.StartIndex];
        first.SetLeadingTrivia(RegionTrivia.RemoveFirstOpen(first.LeadingTrivia, out _));

        // The matching close sits either in the leading trivia of the block after the
        // run, or in the trailing trivia of the run's last block.
        if (span.EndIndex + 1 < blocks.Count)
        {
            var after = blocks[span.EndIndex + 1];
            after.SetLeadingTrivia(RemoveFirstClose(after.LeadingTrivia));
        }
        else
        {
            var last = blocks[span.EndIndex];
            last.SetTrailingTrivia(RemoveFirstClose(last.TrailingTrivia));
        }

        stack.RebuildRegionDecorations();
        first.RaisePublicFieldChanged();
    }

    public static void SetAllCollapsed(BlockStackControl stack, bool collapsed)
    {
        void Walk(IReadOnlyList<RegionSpan> spans)
        {
            foreach (var s in spans) { stack.SetRegionCollapsed(s, collapsed); Walk(s.Children); }
        }
        // Rebuild between mutations is handled inside SetRegionCollapsed; snapshot first.
        Walk(stack.Regions.Roots);
    }

    private static Ast.Trivia RemoveFirstClose(Ast.Trivia t)
    {
        var builder = t.Tokens.ToBuilder();
        for (int i = 0; i < builder.Count; i++)
            if (builder[i] is Ast.LineCommentTrivia c && RegionMarker.IsClose(c.Text)) { builder.RemoveAt(i); break; }
        return new Ast.Trivia(builder.ToImmutable());
    }
}
