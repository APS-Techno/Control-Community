using System.Globalization;
using System.Text;

namespace APS.RecipeModel.Validation;

/// <summary>
/// Blank lines and comment-only lines are stripped from the output.
/// </summary>
public class Lexer
{
    /// <summary>
    /// Characters the most recent <see cref="Tokenize"/> call had to skip.
    /// Validators surface them as errors: a silently dropped minus sign in
    /// `stage HS -283` drove the substrate arm the wrong direction (incident
    /// 2026-05-12, LOAD_SUBSTRATE).
    /// </summary>
    public List<LexerDiagnostic> Diagnostics { get; } = new();

    public List<List<Token>> Tokenize(string recipeText)
    {
        Diagnostics.Clear();
        var result = new List<List<Token>>();
        if (string.IsNullOrEmpty(recipeText))
            return result;

        // Normalise CRLF first: splitting on '\r' AND '\n' separately would count a
        // Windows line ending as TWO lines and shift every reported line number.
        string[] rawLines = recipeText.Replace("\r\n", "\n").Split(['\r', '\n'], StringSplitOptions.None);

        for (int i = 0; i < rawLines.Length; i++)
        {
            string line = rawLines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Split on semicolons (multiple commands per line) — but only in the
            // code part: a ';' inside a comment or a quoted string is text.
            foreach (string subLine in SplitStatements(line))
            {
                if (string.IsNullOrWhiteSpace(subLine))
                    continue;

                var tokens = TokenizeLine(subLine, i + 1);
                if (tokens.Count == 0)
                    continue;

                if (tokens.Count == 1 && tokens[0].Type == TokenType.Comment)
                    continue;

                result.Add(tokens);
            }
        }

        return result;
    }

    /// <summary>
    /// Splits one source line into statements on ';' — ignoring semicolons
    /// inside "quoted strings" and everything from a '#' / '//' comment
    /// start onward. The comment stays attached to the last statement so a
    /// comment-only line is still recognised as such. Mirrors the AST lexer's
    /// <c>SplitStatements</c> (Ast/Lexer/Lexer.cs); the two must agree or
    /// the editor and the runtime read different recipes.
    /// </summary>
    internal static List<string> SplitStatements(string line)
    {
        var segments = new List<string>();
        int segStart = 0;
        bool inString = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inString)
            {
                if (c == '\\' && i + 1 < line.Length) { i++; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '#' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
                break;
            if (c == ';')
            {
                segments.Add(line[segStart..i]);
                segStart = i + 1;
            }
        }

        segments.Add(line[segStart..]);
        return segments;
    }

    private List<Token> TokenizeLine(string line, int lineNumber)
    {
        var tokens = new List<Token>();
        int pos = 0;

        while (pos < line.Length)
        {
            char c = line[pos];

            if (char.IsWhiteSpace(c))
            {
                pos++;
                continue;
            }

            if (c == '#' || (c == '/' && pos + 1 < line.Length && line[pos + 1] == '/'))
            {
                int startCol = pos + 1;
                string commentText = line[pos..];
                tokens.Add(new Token(TokenType.Comment, commentText, lineNumber, startCol));
                break;
            }

            if (c == '"')
            {
                int startCol = pos + 1;
                pos++;
                var sb = new StringBuilder();
                while (pos < line.Length && line[pos] != '"')
                {
                    if (line[pos] == '\\' && pos + 1 < line.Length)
                    {
                        pos++;
                        sb.Append(line[pos] switch
                        {
                            'n' => '\n',
                            't' => '\t',
                            '\\' => '\\',
                            '"' => '"',
                            _ => line[pos]
                        });
                    }
                    else
                    {
                        sb.Append(line[pos]);
                    }
                    pos++;
                }
                if (pos < line.Length) pos++;
                tokens.Add(new Token(TokenType.String, sb.ToString(), lineNumber, startCol));
                continue;
            }

            if (c == '$')
            {
                int startCol = pos + 1;
                pos++;
                int nameStart = pos;
                while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_'))
                    pos++;
                string varName = line[nameStart..pos];
                if (varName.Length == 0)
                {
                    tokens.Add(new Token(TokenType.Dollar, "$", lineNumber, startCol));
                }
                else
                {
                    tokens.Add(new Token(TokenType.Dollar, "$", lineNumber, startCol));
                    tokens.Add(new Token(TokenType.Identifier, varName, lineNumber, startCol + 1));
                }
                continue;
            }

            if (c == '=')
            {
                tokens.Add(new Token(TokenType.Equals, "=", lineNumber, pos + 1));
                pos++;
                continue;
            }
            if (c == ',')
            {
                tokens.Add(new Token(TokenType.Comma, ",", lineNumber, pos + 1));
                pos++;
                continue;
            }
            if (c == '(')
            {
                tokens.Add(new Token(TokenType.OpenParen, "(", lineNumber, pos + 1));
                pos++;
                continue;
            }
            if (c == ')')
            {
                tokens.Add(new Token(TokenType.CloseParen, ")", lineNumber, pos + 1));
                pos++;
                continue;
            }
            if (c == ':')
            {
                tokens.Add(new Token(TokenType.Colon, ":", lineNumber, pos + 1));
                pos++;
                continue;
            }

            if (IsNumberStart(c, line, pos, tokens))
            {
                int startCol = pos + 1;
                int numStart = pos;
                if (c == '-' || c == '+') pos++;
                while (pos < line.Length && char.IsDigit(line[pos]))
                    pos++;
                if (pos < line.Length && line[pos] == '.')
                {
                    pos++;
                    while (pos < line.Length && char.IsDigit(line[pos]))
                        pos++;
                }
                if (pos < line.Length && (line[pos] == 'e' || line[pos] == 'E'))
                {
                    pos++;
                    if (pos < line.Length && (line[pos] == '+' || line[pos] == '-'))
                        pos++;
                    while (pos < line.Length && char.IsDigit(line[pos]))
                        pos++;
                }
                // A unit suffix glued to the number (50mT, 30m) is part of the literal;
                // anything else ("5x") stays a separate Identifier. Pressure is tried
                // first so "5mT" stays a pressure literal.
                int suffixLen = MatchPressureSuffix(line, pos);
                if (suffixLen == 0) suffixLen = MatchTimeSuffix(line, pos);
                if (suffixLen > 0) pos += suffixLen;
                string numStr = line[numStart..pos];
                tokens.Add(new Token(TokenType.Number, numStr, lineNumber, startCol));
                continue;
            }

            // '.' is allowed for hierarchical names.
            if (char.IsLetter(c) || c == '_')
            {
                int startCol = pos + 1;
                int wordStart = pos;
                while (pos < line.Length && (char.IsLetterOrDigit(line[pos]) || line[pos] == '_' || line[pos] == '.'))
                    pos++;
                string word = line[wordStart..pos];

                var type = tokens.Count == 0 ? TokenType.Command : TokenType.Identifier;
                tokens.Add(new Token(type, word, lineNumber, startCol));
                continue;
            }

            if (c == '<' || c == '>' || c == '!')
            {
                int startCol = pos + 1;
                tokens.Add(new Token(TokenType.Identifier, c.ToString(), lineNumber, startCol));
                pos++;
                continue;
            }

            // Unknown character — record it (so callers can surface a real error
            // instead of silent data loss) and skip past to avoid an infinite loop.
            Diagnostics.Add(new LexerDiagnostic(lineNumber, pos + 1, c));
            pos++;
        }

        return tokens;
    }

    /// <summary>
    /// Order matters: longer matches must be tried first so "mTorr" is preferred
    /// over "m".
    /// </summary>
    public static readonly string[] PressureSuffixes =
    {
        "nTorr", "uTorr", "µTorr", "mTorr", "kTorr", "Torr",
        "nT",    "uT",    "µT",    "mT",    "kT",    "T",
    };

    /// <summary>
    /// Returns the length of a pressure-unit suffix starting at <paramref name="pos"/>
    /// in <paramref name="line"/>, or 0 if no suffix matches. The suffix must be
    /// followed by whitespace, end-of-line, or a punctuation character (so a
    /// stray identifier like "stage" doesn't get consumed as the "T" suffix).
    /// </summary>
    public static int MatchPressureSuffix(string line, int pos)
    {
        foreach (var suffix in PressureSuffixes)
        {
            if (pos + suffix.Length > line.Length) continue;
            if (string.CompareOrdinal(line, pos, suffix, 0, suffix.Length) != 0) continue;
            int after = pos + suffix.Length;
            if (after == line.Length) return suffix.Length;
            char next = line[after];
            if (!char.IsLetterOrDigit(next) && next != '_')
                return suffix.Length;
        }
        return 0;
    }

    /// <summary>
    /// Recognised time-unit suffixes a numeric literal may carry directly
    /// (s = seconds, m = minutes, h = hours). Bare numbers are seconds. Kept
    /// symmetric with <see cref="PressureSuffixes"/> so `wait 30m` folds into a
    /// single Number token in both the runtime and AST lexers.
    /// </summary>
    public static readonly string[] TimeSuffixes = { "h", "m", "s" };

    /// <summary>
    /// Returns the length of a time-unit suffix starting at <paramref name="pos"/>,
    /// or 0 if none matches. Same trailing-boundary rule as
    /// <see cref="MatchPressureSuffix"/>: the suffix is valid only when followed
    /// by whitespace, end-of-line, or a non-alphanumeric/non-underscore char, so
    /// an identifier like "state" isn't mistaken for the "s" suffix.
    /// </summary>
    public static int MatchTimeSuffix(string line, int pos)
    {
        foreach (var suffix in TimeSuffixes)
        {
            if (pos + suffix.Length > line.Length) continue;
            if (string.CompareOrdinal(line, pos, suffix, 0, suffix.Length) != 0) continue;
            int after = pos + suffix.Length;
            if (after == line.Length) return suffix.Length;
            char next = line[after];
            if (!char.IsLetterOrDigit(next) && next != '_')
                return suffix.Length;
        }
        return 0;
    }

    /// <summary>
    /// A leading '-' or '+' always belongs to the number when followed by a digit
    /// or '.': the recipe DSL has no subtraction operator. Do not guard on the
    /// previous token — that turned `stage HS -283` into `stage HS 283`
    /// (incident 2026-05-12, LOAD_SUBSTRATE) and stripped negatives from
    /// `if x < -5` comparisons.
    /// </summary>
    private static bool IsNumberStart(char c, string line, int pos, List<Token> _tokens)
    {
        if (char.IsDigit(c))
            return true;

        return (c == '-' || c == '+')
            && pos + 1 < line.Length
            && (char.IsDigit(line[pos + 1]) || line[pos + 1] == '.');
    }
}

/// <summary>
/// A character the tokenizer did not recognise; the validator promotes it to a
/// recipe error.
/// </summary>
public record LexerDiagnostic(int Line, int Column, char Character);
