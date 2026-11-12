using System.Collections.Immutable;
using System.Text;

namespace APS.RecipeModel.Ast;

public readonly record struct LexedLine(
    ImmutableArray<Token> Tokens,
    Trivia LeadingWhitespace,
    Trivia TrailingComment,
    string RawText);

public static class Lexer
{
    /// <summary>
    /// Splits a raw source line into statement segments on top-level ';'
    /// separators, matching the runtime lexer. Semicolons inside a string literal
    /// are not separators, and once a line comment (# or //) begins the rest of the
    /// line — including any ';' — belongs to the current segment.
    /// Returns one segment when the line contains no top-level ';' (blank and
    /// comment-only lines pass through unchanged so their trivia is preserved).
    /// </summary>
    public static List<string> SplitStatements(string line)
    {
        var segments = new List<string>();
        int segStart = 0;
        bool inString = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inString)
            {
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

    public static LexedLine TokenizeLine(string line, int lineNumber)
    {
        var tokens = ImmutableArray.CreateBuilder<Token>();
        int i = 0;

        int wsStart = i;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        var leading = i > wsStart
            ? new Trivia(ImmutableArray.Create<TriviaToken>(new WhitespaceTrivia(line[wsStart..i])))
            : Trivia.Empty;

        Trivia trailing = Trivia.Empty;

        while (i < line.Length)
        {
            char c = line[i];

            if (c == '#' || (c == '/' && i + 1 < line.Length && line[i + 1] == '/'))
            {
                trailing = new Trivia(ImmutableArray.Create<TriviaToken>(
                    new LineCommentTrivia(line[i..])));
                break;
            }

            // Intra-token whitespace is not recorded — canonical spacing is restored on print.
            if (c == ' ' || c == '\t') { i++; continue; }

            if (c == '"')
            {
                int start = ++i;
                var sb = new StringBuilder();
                while (i < line.Length && line[i] != '"')
                {
                    sb.Append(line[i]);
                    i++;
                }
                tokens.Add(new Token(TokenKind.StringLiteral, sb.ToString(),
                    new SourceSpan(lineNumber, start - 1, i - start + 2)));
                if (i < line.Length) i++;
                continue;
            }

            if (c == '$')
            {
                int start = i++;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.ParamRef, line[(start + 1)..i],
                    new SourceSpan(lineNumber, start, i - start)));
                continue;
            }

            // Multi-char operators first
            if (i + 1 < line.Length)
            {
                var pair = line[i..(i + 2)];
                if (pair is "==" or "!=" or "<=" or ">=")
                {
                    tokens.Add(new Token(TokenKind.Operator, pair,
                        new SourceSpan(lineNumber, i, 2)));
                    i += 2;
                    continue;
                }
            }

            // Single-char operators — but don't eat '-' when followed by a digit;
            // the number rule below will handle it as a negative literal.
            bool isNegativeNumber = c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1]);
            if (!isNegativeNumber && "<>=+-(),".IndexOf(c) >= 0)
            {
                tokens.Add(new Token(TokenKind.Operator, c.ToString(),
                    new SourceSpan(lineNumber, i, 1)));
                i++;
                continue;
            }

            // Number: optional -, digits, optional fractional part, optional unit suffix
            // (e.g. "50mT", "5kT") — letters contiguous with digits stay in the token
            // so pressure literals round-trip cleanly through pwait/swait.
            if (char.IsDigit(c) || (c == '-' && i + 1 < line.Length && char.IsDigit(line[i + 1])))
            {
                int start = i;
                if (c == '-') i++;
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                while (i < line.Length && char.IsLetter(line[i])) i++;
                tokens.Add(new Token(TokenKind.Number, line[start..i],
                    new SourceSpan(lineNumber, start, i - start)));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < line.Length && (char.IsLetterOrDigit(line[i]) || line[i] == '_')) i++;
                tokens.Add(new Token(TokenKind.Identifier, line[start..i],
                    new SourceSpan(lineNumber, start, i - start)));
                continue;
            }

            // Unrecognized — skip to avoid infinite loop
            i++;
        }

        return new LexedLine(tokens.ToImmutable(), leading, trailing, line);
    }
}
