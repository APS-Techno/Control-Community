using APS.RecipeModel.Ast;
using Xunit;

namespace APS.RecipeModel.Tests.Ast;

public class RegionRoundTripTests
{
    private static string RoundTrip(string src) => PrettyPrinter.Print(Parser.Parse(src));

    [Fact]
    public void Region_markers_survive_text_ast_text()
    {
        string src =
            "#region Ar sputter [collapsed]\n" +
            "valve M1 open\n" +
            "mfc Ar1 20\n" +
            "#endregion\n" +
            "valve M2 close\n";
        Assert.Equal(src, RoundTrip(src));
    }

    [Fact]
    public void Region_close_in_trailing_trivia_prints_on_its_own_line()
    {
        // #endregion attaches to the last statement's trailing trivia (region
        // ending at the last block of the script); it must serialize as a
        // full-line comment, not glued onto the statement line.
        string src =
            "#region tail\n" +
            "valve M1 open\n" +
            "mfc Ar1 20 #endregion\n";
        string expected =
            "#region tail\n" +
            "valve M1 open\n" +
            "mfc Ar1 20\n" +
            "#endregion\n";
        Assert.Equal(expected, RoundTrip(src));
    }

    [Fact]
    public void Region_ending_the_script_on_its_own_line_is_stable()
    {
        string src =
            "#region tail\n" +
            "valve M1 open\n" +
            "mfc Ar1 20\n" +
            "#endregion\n";
        Assert.Equal(src, RoundTrip(src));
    }

    [Fact]
    public void Plain_trailing_comment_still_prints_inline()
    {
        string src =
            "valve M1 open # user note\n" +
            "mfc Ar1 20\n";
        Assert.Equal(src, RoundTrip(src));
    }
}
