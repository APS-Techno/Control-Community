using System.Collections.Generic;
using System.Linq;
using System.Net;
using APS.RecipeModel.Serialization;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class ScriptLineMapTests
{
    private const int ParamCount = 11;
    private const int ScriptTagLine = 25;

    private static string Build(string newline, params string[] scriptElementLines)
    {
        var lines = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<Recipe>",
            "\t<Description type=\"Deposition\">",
            "\t\t<Title>t</Title>",
            "\t\t<Author>a</Author>",
            "\t\t<Created>2026-01-01</Created>",
            "\t\t<Modified>2026-01-01</Modified>",
            "\t\t<Notes></Notes>",
            "\t</Description>",
            "\t<Parameters>",
            "\t\t<Group name=\"g\">",
        };
        for (int i = 1; i <= ParamCount; i++)
            lines.Add($"\t\t\t<Param name=\"P{i}\" caption=\"c\" unit=\"s\" type=\"float\">{i}</Param>");
        lines.Add("\t\t</Group>");
        lines.Add("\t</Parameters>");
        lines.AddRange(scriptElementLines);
        lines.Add("</Recipe>");
        return string.Join(newline, lines);
    }

    private static void AssertEveryScriptLineMapsToItsText(string xml)
    {
        var recipe = RecipeSerializer.LoadFromString(xml);
        int p = ScriptLineMap.CountLineBreaks(recipe.GenerateVarBlock());
        Assert.Equal(ParamCount, p);

        var map = ScriptLineMap.ForRcpx(xml, p);
        var script = recipe.Script.Replace("\r\n", "\n").Split('\n');
        var raw = xml.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < script.Length; i++)
        {
            int? xmlLine = map(p + i + 1);
            Assert.NotNull(xmlLine);
            Assert.Contains(WebUtility.HtmlDecode(script[i].Trim()), WebUtility.HtmlDecode(raw[xmlLine.Value - 1]));
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Script_line_45_with_eleven_params_maps_to_xml_line_59(string newline)
    {
        var script = new List<string> { "<Script>" };
        for (int i = 1; i <= 33; i++) script.Add($"# filler {i}");
        script.Add("# cap layer from the old recipe, not used here; deposit C 33 Home");
        script.Add("deposit Ru 5 Home");
        script.Add("</Script>");
        string xml = Build(newline, script.ToArray());

        var recipe = RecipeSerializer.LoadFromString(xml);
        var map = ScriptLineMap.ForRcpx(xml, ScriptLineMap.CountLineBreaks(recipe.GenerateVarBlock()));
        var summary = RecipeScriptSummary.Compute(recipe.FullScript);

        Assert.Equal(new[] { 45 }, summary.InertCommentLinesWithSemicolon);
        Assert.Equal(59, map(45));
        Assert.Equal(ScriptTagLine + 1, map(ParamCount + 1));
        AssertEveryScriptLineMapsToItsText(xml);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Leading_blank_lines_and_indentation_of_the_script_text_are_skipped(string newline)
    {
        string xml = Build(newline, "\t<Script>", "", "", "\t\t# a", "\t\tdeposit Ru 5", "\t</Script>");

        Assert.Equal(ScriptTagLine + 3, ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
        AssertEveryScriptLineMapsToItsText(xml);
    }

    [Fact]
    public void Text_on_the_same_line_as_the_script_tag_maps_to_that_line()
    {
        string xml = Build("\n", "<Script>deposit Ru 5", "loop 2", "endloop</Script>");

        Assert.Equal(ScriptTagLine, ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
        AssertEveryScriptLineMapsToItsText(xml);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Cdata_section_opened_on_its_own_line_is_skipped(string newline)
    {
        string xml = Build(newline, "<Script><![CDATA[", "deposit Ru 5 # a < b", "loop 2", "]]></Script>");

        Assert.Equal(ScriptTagLine + 1, ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
        AssertEveryScriptLineMapsToItsText(xml);
    }

    [Fact]
    public void Escaped_text_keeps_its_line()
    {
        string xml = Build("\n", "<Script>", "if x &lt; 5", "deposit Ru 5", "endif", "</Script>");

        Assert.Equal(ScriptTagLine + 1, ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
        AssertEveryScriptLineMapsToItsText(xml);
    }

    [Fact]
    public void Lines_inside_the_var_block_have_no_xml_line()
    {
        string xml = Build("\n", "<Script>", "deposit Ru 5", "</Script>");
        var map = ScriptLineMap.ForRcpx(xml, ParamCount);

        Assert.Null(map(0));
        Assert.Null(map(1));
        Assert.Null(map(ParamCount));
        Assert.NotNull(map(ParamCount + 1));
    }

    [Fact]
    public void Empty_script_element_maps_nothing()
    {
        string xml = Build("\n", "<Script/>");

        Assert.Null(ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
    }

    [Fact]
    public void Whitespace_only_script_maps_nothing()
    {
        string xml = Build("\n", "<Script>", "   ", "</Script>");

        Assert.Null(ScriptLineMap.ForRcpx(xml, ParamCount)(ParamCount + 1));
    }

    [Fact]
    public void Count_line_breaks_treats_crlf_as_one()
    {
        Assert.Equal(3, ScriptLineMap.CountLineBreaks("a\r\nb\nc\rd"));
        Assert.Equal(0, ScriptLineMap.CountLineBreaks(""));
    }
}
