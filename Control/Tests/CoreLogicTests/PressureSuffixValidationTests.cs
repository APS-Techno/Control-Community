using APS.RecipeModel.Validation;

namespace CoreLogicTests;

/// <summary>
/// Locks in the bare-pressure-value warning for pwait/swait target
/// positions. Bare numbers are interpreted as Torr at runtime — recipes
/// authored in mTorr (the historical Delphi convention) silently complete
/// at the wrong pressure unless the value carries an explicit unit suffix.
/// </summary>
public class PressureSuffixValidationTests
{
    private static RecipeValidator V() => new(
        RecipeValidator.AllKnownCommands,
        knownDevices: ["pm", "pl"]);

    [Theory]
    [InlineData("pwait pm 2 300")]
    [InlineData("swait pm 50 300")]
    [InlineData("swait pl 0.05 300")]
    [InlineData("pwait pm 1.5e-3 300")]
    public void Bare_pressure_value_emits_warning(string script)
    {
        var findings = V().Validate(script);
        var warnings = findings.Where(f => f.Severity == RecipeErrorSeverity.Warning).ToList();
        Assert.Single(warnings);
        Assert.Contains("interpreted as Torr", warnings[0].Message);
    }

    [Fact]
    public void Warning_message_suggests_suffix_alternatives()
    {
        var findings = V().Validate("pwait pm 2 300");
        var w = findings.Single(f => f.Severity == RecipeErrorSeverity.Warning);
        Assert.Contains("'2mT'", w.Message);
        Assert.Contains("'2Torr'", w.Message);
    }

    [Theory]
    [InlineData("pwait pm 2mT 300")]
    [InlineData("pwait pm 2Torr 300")]
    [InlineData("swait pm 1uT 1800")]
    [InlineData("swait pm 100nT 60")]
    [InlineData("swait pm 1µT 60")]
    [InlineData("pwait pm 1e-6T 60")]
    [InlineData("pwait pm 50mTorr 60")]
    public void Suffixed_pressure_value_passes(string script)
    {
        var findings = V().Validate(script);
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Warning);
    }

    [Fact]
    public void Target_zero_does_not_warn()
    {
        // pwait pm 0 X means "open the throttle valve" — unit-agnostic.
        var findings = V().Validate("pwait pm 0 60");
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Warning);
    }

    [Fact]
    public void Variable_substitution_does_not_warn()
    {
        // The substituted value resolves at runtime and may carry its own suffix
        // (e.g. via RecipeParamForm appending the param's unit attribute).
        var findings = V().Validate("var P = 5\npwait pm $P 60");
        Assert.DoesNotContain(findings, f => f.Severity == RecipeErrorSeverity.Warning);
    }

    [Theory]
    [InlineData("50mT")]
    [InlineData("1uTorr")]
    [InlineData("1.5e-3T")]
    [InlineData("100nT")]
    public void Lexer_keeps_suffix_with_number_token(string token)
    {
        var lex = new Lexer();
        var lines = lex.Tokenize($"swait pm {token} 60");
        var tokens = lines.Single();
        // ["swait", "pm", "<numWithSuffix>", "60"] — 4 tokens, not 5.
        Assert.Equal(4, tokens.Count);
        Assert.Equal(TokenType.Number, tokens[2].Type);
        Assert.Equal(token, tokens[2].Value);
    }

    [Fact]
    public void Lexer_does_not_consume_unrelated_identifier()
    {
        // "5x" must NOT consume "x" as a suffix — only known pressure suffixes.
        var lex = new Lexer();
        var lines = lex.Tokenize("delay 5x");
        var tokens = lines.Single();
        Assert.Equal(3, tokens.Count);     // "delay", "5", "x"
        Assert.Equal("5", tokens[1].Value);
        Assert.Equal("x", tokens[2].Value);
    }

    [Fact]
    public void Default_severity_is_Error()
    {
        var err = new RecipeError(1, 1, "msg");
        Assert.Equal(RecipeErrorSeverity.Error, err.Severity);
    }
}
