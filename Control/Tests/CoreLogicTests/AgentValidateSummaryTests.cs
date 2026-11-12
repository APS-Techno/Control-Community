using System.Text.Json;
using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentValidateSummaryTests
{
    private const string Script =
        "deposit Ru 418.17\n" +
        "loop 3\n" +
        "deposit Ru 312.6\n" +
        "deposit C 5 Home\n" +
        "endloop\n" +
        "# cap layer; deposit C 33 Home";

    private sealed class ReportSys(ValidationReport report) : AgentDispatcherTests.FakeSysBase
    {
        public override ValidationReport ValidateWithSummary(string content, bool isRcpx)
        {
            Calls.Add("validate");
            return report;
        }
    }

    private static JsonElement Validate(ValidationReport report, out ReportSys sys)
    {
        sys = new ReportSys(report);
        var d = new AgentDispatcher(sys);
        var root = JsonDocument.Parse(d.Handle(
            """{"v":1,"id":1,"cmd":"recipe.validate","args":{"content":"x"}}""", AgentTier.Read, "r")).RootElement;
        Assert.True(root.GetProperty("ok").GetBoolean());
        return root.GetProperty("data");
    }

    private static ValidationReport Rcpx() => new(
        [
            new RecipeError(15, 1, "rotation is not operational", RecipeErrorSeverity.Warning),
            new RecipeError(0, 0, "catalogue empty"),
        ],
        RecipeScriptSummary.Compute(Script),
        line => line > 0 ? line + 14 : null);

    [Fact]
    public void Findings_keep_their_keys_and_gain_xmlLine_when_known()
    {
        var data = Validate(Rcpx(), out var sys);

        Assert.Contains("validate", sys.Calls);
        Assert.False(data.GetProperty("valid").GetBoolean());

        var first = data.GetProperty("findings")[0];
        Assert.Equal(15, first.GetProperty("line").GetInt32());
        Assert.Equal(1, first.GetProperty("column").GetInt32());
        Assert.Equal("warning", first.GetProperty("severity").GetString());
        Assert.Equal("rotation is not operational", first.GetProperty("message").GetString());
        Assert.Equal(29, first.GetProperty("xmlLine").GetInt32());

        var second = data.GetProperty("findings")[1];
        Assert.Equal(0, second.GetProperty("line").GetInt32());
        Assert.False(second.TryGetProperty("xmlLine", out _));
    }

    [Fact]
    public void Warnings_alone_leave_the_recipe_valid()
    {
        var report = new ValidationReport([new RecipeError(3, 1, "w", RecipeErrorSeverity.Warning)],
            RecipeScriptSummary.Compute(""), null);

        Assert.True(Validate(report, out _).GetProperty("valid").GetBoolean());
    }

    [Fact]
    public void ScriptSummary_has_the_documented_keys_and_values()
    {
        var summary = Validate(Rcpx(), out _).GetProperty("scriptSummary");

        var ru = summary.GetProperty("depositSecondsBySource").GetProperty("Ru");
        Assert.Equal(418.17, ru.GetProperty("outsideLoopSeconds").GetDouble());
        Assert.Equal(937.8, ru.GetProperty("inLoopSeconds").GetDouble());
        Assert.Equal(1355.97, ru.GetProperty("totalSeconds").GetDouble());

        var c = summary.GetProperty("depositSecondsBySource").GetProperty("C");
        Assert.Equal(0.0, c.GetProperty("outsideLoopSeconds").GetDouble());
        Assert.Equal(15.0, c.GetProperty("inLoopSeconds").GetDouble());
        Assert.Equal(15.0, c.GetProperty("totalSeconds").GetDouble());

        Assert.Equal(1370.97, summary.GetProperty("totalDepositSeconds").GetDouble());
        Assert.False(summary.GetProperty("hasUnresolvedVariables").GetBoolean());
        Assert.Equal(
            "pwait/swait timeout arguments (e.g. 600 s, 10 s) are upper-bound ceilings, not fixed durations; " +
            "total wall-clock run time cannot be determined without running the recipe.",
            summary.GetProperty("pwaitTimeoutsNote").GetString());
        Assert.Equal(
            "All text after '#' or '//' on a line (including ';' characters) is stripped before statement " +
            "splitting and is completely inert.",
            summary.GetProperty("commentsNote").GetString());

        var inert = summary.GetProperty("inertCommentLinesWithSemicolon");
        Assert.Equal(1, inert.GetArrayLength());
        Assert.Equal(6, inert[0].GetProperty("scriptLine").GetInt32());
        Assert.Equal(20, inert[0].GetProperty("xmlLine").GetInt32());
    }

    [Fact]
    public void Bare_dsl_content_reports_script_lines_only()
    {
        var report = new ValidationReport([new RecipeError(2, 1, "bad")], RecipeScriptSummary.Compute(Script), null);

        var data = Validate(report, out _);

        Assert.False(data.GetProperty("findings")[0].TryGetProperty("xmlLine", out _));
        var inert = data.GetProperty("scriptSummary").GetProperty("inertCommentLinesWithSemicolon")[0];
        Assert.Equal(6, inert.GetProperty("scriptLine").GetInt32());
        Assert.False(inert.TryGetProperty("xmlLine", out _));
    }

    [Fact]
    public void Unresolved_variables_and_empty_summary_serialize()
    {
        var report = new ValidationReport([], RecipeScriptSummary.Compute("deposit Ru $missing"), null);

        var summary = Validate(report, out _).GetProperty("scriptSummary");

        Assert.True(summary.GetProperty("hasUnresolvedVariables").GetBoolean());
        Assert.Equal(0, summary.GetProperty("depositSecondsBySource").EnumerateObject().Count());
        Assert.Equal(0.0, summary.GetProperty("totalDepositSeconds").GetDouble());
        Assert.Equal(0, summary.GetProperty("inertCommentLinesWithSemicolon").GetArrayLength());
    }
}
