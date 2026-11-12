using APS.RecipeModel.Blocks;
using Xunit;

namespace APS.RecipeModel.Tests.Blocks;

public class RegionMarkerTests
{
    [Theory]
    [InlineData("#region Ar sputter", "Ar sputter", false)]
    [InlineData("# region Ar sputter", "Ar sputter", false)]
    [InlineData("//region Ar sputter [collapsed]", "Ar sputter", true)]
    [InlineData("#region  Ar sputter  [collapsed]", "Ar sputter", true)]
    [InlineData("#REGION main [COLLAPSED]", "main", true)]
    [InlineData("#region", "", false)]
    public void TryParseOpen_parses_name_and_flag(string text, string name, bool collapsed)
    {
        var open = RegionMarker.TryParseOpen(text);
        Assert.NotNull(open);
        Assert.Equal(name, open!.Value.Name);
        Assert.Equal(collapsed, open.Value.Collapsed);
    }

    [Theory]
    [InlineData("#regionize foo")] // "region" must be a whole word
    [InlineData("# just a comment")]
    [InlineData("#endregion")]
    public void TryParseOpen_rejects_non_open(string text)
        => Assert.Null(RegionMarker.TryParseOpen(text));

    [Theory]
    [InlineData("#endregion", true)]
    [InlineData("//endregion Ar sputter", true)]
    [InlineData("#endregionx", false)]
    [InlineData("#region foo", false)]
    public void IsClose_detects_close(string text, bool expected)
        => Assert.Equal(expected, RegionMarker.IsClose(text));

    [Theory]
    [InlineData("Ar sputter", false, "#region Ar sputter")]
    [InlineData("Ar sputter", true, "#region Ar sputter [collapsed]")]
    [InlineData("", true, "#region [collapsed]")]
    [InlineData("  ", false, "#region")]
    public void FormatOpen_roundtrips(string name, bool collapsed, string expected)
        => Assert.Equal(expected, RegionMarker.FormatOpen(name, collapsed));

    [Theory]
    [InlineData("#region Ar sputter [collapsed]", true)]
    [InlineData("  //endregion", true)]
    [InlineData("# just a comment", false)]
    public void RegionLineRegex_matches_only_markers(string line, bool expected)
        => Assert.Equal(expected, APS.RecipeModel.Controls.ScriptEditor.ScriptHighlighter.RegionLineRegex.IsMatch(line));
}
