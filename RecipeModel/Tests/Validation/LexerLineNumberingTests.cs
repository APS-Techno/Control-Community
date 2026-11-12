using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

/// <summary>
/// Line numbers reported by the lexer (and therefore by every validator phase)
/// must count a CRLF pair as ONE line. RecipeFile.GenerateVarBlock emits CRLF, so
/// splitting on '\r' and '\n' separately puts RecipeEditor's error grid off by 2N
/// for a recipe with N parameters.
/// </summary>
public class LexerLineNumberingTests
{
    [Fact]
    public void Crlf_line_endings_count_as_one_line()
    {
        var lines = new Lexer().Tokenize("var a 1\r\nvar b 2\r\nvalve HV open");

        Assert.Equal(3, lines.Count);
        Assert.Equal(1, lines[0][0].Line);
        Assert.Equal(2, lines[1][0].Line);
        Assert.Equal(3, lines[2][0].Line);
    }

    [Fact]
    public void Lf_and_crlf_scripts_report_identical_line_numbers()
    {
        var lf   = new Lexer().Tokenize("var a 1\nvalve HV open\nbogus X");
        var crlf = new Lexer().Tokenize("var a 1\r\nvalve HV open\r\nbogus X");

        Assert.Equal(lf.Select(l => l[0].Line), crlf.Select(l => l[0].Line));
    }

    [Fact]
    public void Validator_error_line_matches_editor_line_for_crlf_var_block()
    {
        // Mirrors RecipeEditor: FullScript = CRLF var block + script.
        var validator = new RecipeValidator(RecipeValidator.AllKnownCommands, new HashSet<string>());
        var errors = validator.Validate("var a 1\r\nvar b 2\r\nvalve HV open\nnotacommand X");

        var err = Assert.Single(errors, e => e.Message.Contains("notacommand", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(4, err.Line);
    }
}
