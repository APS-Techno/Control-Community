using System.Linq;
using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class RecipeScriptSummaryTests
{
    [Fact]
    public void Three_arg_deposits_with_var_and_nested_loops_multiply()
    {
        const string script = """
            var T 100
            var N 3
            deposit Ru 10 Home
            loop $N
              deposit Ru $T Home
              loop 2
                deposit C 5s 1
              endloop
            endloop
            """;

        var s = RecipeScriptSummary.Compute(script);

        Assert.Equal(2, s.DepositSecondsBySource.Count);
        Assert.Equal((10.0, 300.0, 310.0), s.DepositSecondsBySource["Ru"]);
        Assert.Equal((0.0, 30.0, 30.0), s.DepositSecondsBySource["C"]);
        Assert.Equal(340.0, s.TotalDepositSeconds);
        Assert.False(s.HasUnresolvedVariables);
    }

    [Fact]
    public void Two_arg_deposit_accepts_time_units()
    {
        var s = RecipeScriptSummary.Compute("deposit Ru 1m\ndeposit Ru 30\ndeposit C 0.5h");

        Assert.Equal((90.0, 0.0, 90.0), s.DepositSecondsBySource["Ru"]);
        Assert.Equal((1800.0, 0.0, 1800.0), s.DepositSecondsBySource["C"]);
        Assert.Equal(1890.0, s.TotalDepositSeconds);
    }

    [Fact]
    public void Bare_identifier_resolves_like_a_dollar_reference()
    {
        var s = RecipeScriptSummary.Compute("var Dwell 20\nvar Stacks 4\nloop Stacks\ndeposit Ru Dwell\nendloop");

        Assert.Equal((0.0, 80.0, 80.0), s.DepositSecondsBySource["Ru"]);
        Assert.False(s.HasUnresolvedVariables);
    }

    [Fact]
    public void Variable_assignment_applies_from_the_line_it_appears_on()
    {
        var s = RecipeScriptSummary.Compute("var T 10\ndeposit Ru $T\nvar T 25\ndeposit Ru $T");

        Assert.Equal(35.0, s.DepositSecondsBySource["Ru"].Total);
    }

    [Fact]
    public void Variable_value_may_reference_an_earlier_variable()
    {
        var s = RecipeScriptSummary.Compute("var A 12\nvar B = $A\ndeposit Ru $B");

        Assert.Equal(12.0, s.DepositSecondsBySource["Ru"].Total);
        Assert.False(s.HasUnresolvedVariables);
    }

    [Fact]
    public void Unresolved_variable_sets_the_flag_and_does_not_throw()
    {
        var s = RecipeScriptSummary.Compute("loop $count\ndeposit Ru $dwell\ndeposit C 7\nendloop");

        Assert.True(s.HasUnresolvedVariables);
        Assert.False(s.DepositSecondsBySource.ContainsKey("Ru"));
        Assert.Equal(7.0, s.DepositSecondsBySource["C"].Total);
    }

    [Fact]
    public void Unresolved_source_variable_sets_the_flag()
    {
        var s = RecipeScriptSummary.Compute("deposit $src 10");

        Assert.True(s.HasUnresolvedVariables);
        Assert.Empty(s.DepositSecondsBySource);
        Assert.Equal(0.0, s.TotalDepositSeconds);
    }

    [Fact]
    public void Unmatched_endloop_and_empty_script_are_harmless()
    {
        Assert.Empty(RecipeScriptSummary.Compute("").DepositSecondsBySource);
        Assert.Equal(5.0, RecipeScriptSummary.Compute("endloop\ndeposit Ru 5").TotalDepositSeconds);
    }

    [Fact]
    public void Loop_with_zero_count_contributes_nothing()
    {
        var s = RecipeScriptSummary.Compute("loop 0\ndeposit Ru 5\nendloop");

        Assert.Equal(0.0, s.TotalDepositSeconds);
    }

    [Fact]
    public void Semicolon_inside_comment_is_listed_and_the_commented_deposit_is_not_counted()
    {
        const string script = "# cap layer; deposit C 33 Home\n" +
                              "deposit Ru 5 // trailing; note\n" +
                              "deposit Ru 5 ; deposit C 7\n" +
                              "valve M1 open";

        var s = RecipeScriptSummary.Compute(script);

        Assert.Equal(new[] { 1, 2 }, s.InertCommentLinesWithSemicolon);
        Assert.Equal(10.0, s.DepositSecondsBySource["Ru"].Total);
        Assert.Equal(7.0, s.DepositSecondsBySource["C"].Total);
    }

    [Fact]
    public void Semicolon_inside_a_quoted_string_is_not_a_comment_semicolon()
    {
        var s = RecipeScriptSummary.Compute("message \"a # b; c\"\nmessage \"x; y\"");

        Assert.Empty(s.InertCommentLinesWithSemicolon);
    }

    [Fact]
    public void Line_numbers_count_crlf_as_one_line()
    {
        var s = RecipeScriptSummary.Compute("valve M1 open\r\n# a; b\r\nvalve M1 close");

        Assert.Equal(new[] { 2 }, s.InertCommentLinesWithSemicolon);
    }

    [Fact]
    public void Comment_semicolon_is_never_a_validator_finding()
    {
        var findings = new RecipeValidator(RecipeValidator.AllKnownCommands, knownDevices: [])
            .Validate("# note; frobnicate 1\n");

        Assert.Empty(findings);
        Assert.Equal(new[] { 1 }, RecipeScriptSummary.Compute("# note; frobnicate 1\n").InertCommentLinesWithSemicolon);
    }

    [Fact]
    public void Statement_split_and_summary_agree_on_where_the_comment_starts()
    {
        const string line = "deposit Ru 5 # a; deposit C 9";

        Assert.Single(new Lexer().Tokenize(line));
        var s = RecipeScriptSummary.Compute(line);
        Assert.False(s.DepositSecondsBySource.ContainsKey("C"));
        Assert.Equal(new[] { 1 }, s.InertCommentLinesWithSemicolon);
    }
}
