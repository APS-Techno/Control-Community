using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

/// <summary>
/// Signed numeric literals after an Identifier (e.g. `stage HS -283`) must keep
/// their sign: the recipe DSL has no subtraction operator, and a dropped sign has
/// driven stages the wrong way (incident 2026-05-12, LOAD_SUBSTRATE).
/// </summary>
public class LexerTests
{
    [Fact]
    public void Stage_negative_position_after_identifier_keeps_sign()
    {
        var lines = new Lexer().Tokenize("stage HS -283");

        var line = Assert.Single(lines);
        Assert.Equal(3, line.Count);
        Assert.Equal(TokenType.Command, line[0].Type);
        Assert.Equal("stage", line[0].Value);
        Assert.Equal(TokenType.Identifier, line[1].Type);
        Assert.Equal("HS", line[1].Value);
        Assert.Equal(TokenType.Number, line[2].Type);
        Assert.Equal("-283", line[2].Value);
    }

    [Fact]
    public void Stage_small_negative_position_keeps_sign()
    {
        var lines = new Lexer().Tokenize("stage VS -10");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.Number, line[2].Type);
        Assert.Equal("-10", line[2].Value);
    }

    [Fact]
    public void Positive_signed_literal_after_identifier_keeps_sign()
    {
        var lines = new Lexer().Tokenize("stage HS +15");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.Number, line[2].Type);
        Assert.Equal("+15", line[2].Value);
    }

    [Fact]
    public void Negative_literal_in_if_condition_keeps_sign()
    {
        // `<` is tokenized as an Identifier, so a guarded IsNumberStart would
        // strip the sign off `-5` here too.
        var lines = new Lexer().Tokenize("if sensorA < -5");

        var line = Assert.Single(lines);
        var last = line[^1];
        Assert.Equal(TokenType.Number, last.Type);
        Assert.Equal("-5", last.Value);
    }

    [Fact]
    public void Negative_literal_after_command_still_works()
    {
        var lines = new Lexer().Tokenize("bias -100");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.Number, line[1].Type);
        Assert.Equal("-100", line[1].Value);
    }

    [Fact]
    public void Negative_decimal_literal_after_identifier_keeps_sign()
    {
        var lines = new Lexer().Tokenize("stage HS -1.5");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.Number, line[2].Type);
        Assert.Equal("-1.5", line[2].Value);
    }

    [Fact]
    public void Unknown_character_is_recorded_as_diagnostic()
    {
        // `@` matches no token kind.
        var lexer = new Lexer();
        lexer.Tokenize("stage HS @15");

        var diag = Assert.Single(lexer.Diagnostics);
        Assert.Equal('@', diag.Character);
    }

    [Fact]
    public void Diagnostics_clear_between_tokenize_calls()
    {
        var lexer = new Lexer();
        lexer.Tokenize("stage HS @15");
        Assert.NotEmpty(lexer.Diagnostics);

        lexer.Tokenize("stage HS -15");
        Assert.Empty(lexer.Diagnostics);
    }

    [Theory]
    [InlineData("wait 30s", "30s")]
    [InlineData("wait 30m", "30m")]
    [InlineData("wait 1h",  "1h")]
    [InlineData("delay 90m", "90m")]
    public void Time_suffix_folds_into_single_number_token(string src, string expected)
    {
        var lines = new Lexer().Tokenize(src);

        var line = Assert.Single(lines);
        Assert.Equal(2, line.Count);                       // command + one number arg
        Assert.Equal(TokenType.Number, line[1].Type);
        Assert.Equal(expected, line[1].Value);
    }

    [Fact]
    public void Pwait_pressure_and_time_suffixes_coexist()
    {
        var lines = new Lexer().Tokenize("pwait pm 5mT 2m");

        var line = Assert.Single(lines);
        Assert.Equal(4, line.Count);
        Assert.Equal(TokenType.Number, line[2].Type);
        Assert.Equal("5mT", line[2].Value);
        Assert.Equal(TokenType.Number, line[3].Type);
        Assert.Equal("2m", line[3].Value);
    }

    [Fact]
    public void Semicolon_inside_comment_does_not_start_a_statement()
    {
        // The AST lexer stops splitting at a comment; the runtime lexer must agree.
        var lines = new Lexer().Tokenize("wait 1 # a; b");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.Command, line[0].Type);
        Assert.Equal("wait", line[0].Value);
        Assert.Equal(TokenType.Number, line[1].Type);
        Assert.Equal(TokenType.Comment, line[2].Type);
        Assert.Equal("# a; b", line[2].Value);
    }

    [Fact]
    public void Semicolon_splits_code_before_a_comment_but_not_inside_it()
    {
        var lines = new Lexer().Tokenize("valve V1 open; valve V2 open # x; y");

        Assert.Equal(2, lines.Count);
        Assert.Equal("V1", lines[0][1].Value);
        Assert.Equal("V2", lines[1][1].Value);
        Assert.Equal(TokenType.Comment, lines[1][^1].Type);
    }

    [Fact]
    public void Semicolon_inside_a_quoted_string_does_not_split()
    {
        var lines = new Lexer().Tokenize("message \"a; b\"");

        var line = Assert.Single(lines);
        Assert.Equal(TokenType.String, line[1].Type);
        Assert.Equal("a; b", line[1].Value);
    }
}
