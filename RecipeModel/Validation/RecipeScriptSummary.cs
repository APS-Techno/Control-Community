using System.Globalization;
using APS.RecipeModel.Parsing;

namespace APS.RecipeModel.Validation;

public sealed record ScriptSummary(
    IReadOnlyDictionary<string, (double OutsideLoop, double InLoop, double Total)> DepositSecondsBySource,
    double TotalDepositSeconds,
    bool HasUnresolvedVariables,
    IReadOnlyList<int> InertCommentLinesWithSemicolon);

public static class RecipeScriptSummary
{
    private readonly record struct Arg(string Text, string? VarName, bool IsWord, int Column, int Length, bool Mergeable);

    public static ScriptSummary Compute(string fullScript)
    {
        string script = fullScript ?? "";
        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var enclosing = new Stack<double>();
        double multiplier = 1;
        bool unresolved = false;
        var bySource = new Dictionary<string, (double Outside, double InLoop)>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in new Lexer().Tokenize(script))
        {
            if (line[0].Type != TokenType.Command) continue;

            switch (line[0].Value.ToLowerInvariant())
            {
                case "var":
                    DeclareVariable(line, vars);
                    break;

                case "loop":
                {
                    var args = Arguments(line);
                    int count = 1;
                    string? text = args.Count > 0 ? Resolve(args[0], vars) : null;
                    if (text is not null
                        && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double n)
                        && double.IsFinite(n))
                        count = (int)Math.Max(0, Math.Round(n));
                    else
                        unresolved = true;
                    enclosing.Push(multiplier);
                    multiplier *= count;
                    break;
                }

                case "endloop":
                    if (enclosing.Count > 0) multiplier = enclosing.Pop();
                    break;

                case "deposit":
                {
                    var args = Arguments(line);
                    if (args.Count < 2) break;

                    string? source = Resolve(args[0], vars);
                    string? time = Resolve(args[1], vars);
                    if (source is null || time is null || !TimeParser.TryParseSeconds(time, out float seconds))
                    {
                        unresolved = true;
                        break;
                    }

                    bySource.TryGetValue(source, out var acc);
                    double total = seconds * multiplier;
                    bySource[source] = enclosing.Count > 0
                        ? (acc.Outside, acc.InLoop + total)
                        : (acc.Outside + total, acc.InLoop);
                    break;
                }
            }
        }

        var perSource = new Dictionary<string, (double OutsideLoop, double InLoop, double Total)>(
            StringComparer.OrdinalIgnoreCase);
        double sum = 0;
        foreach (var (source, (outside, inLoop)) in bySource)
        {
            perSource[source] = (outside, inLoop, outside + inLoop);
            sum += outside + inLoop;
        }

        return new ScriptSummary(perSource, sum, unresolved, InertCommentLinesWithSemicolon(script));
    }

    private static void DeclareVariable(List<Token> line, Dictionary<string, string> vars)
    {
        if (line.Count < 3 || line[1].Type != TokenType.Identifier) return;

        string name = line[1].Value;
        int i = 2;
        if (line[i].Type == TokenType.Equals) i++;
        if (i >= line.Count) return;

        var value = line[i];
        switch (value.Type)
        {
            case TokenType.Number or TokenType.String or TokenType.Identifier:
                vars[name] = value.Value;
                break;
            case TokenType.Dollar when i + 1 < line.Count && line[i + 1].Type == TokenType.Identifier
                                       && vars.TryGetValue(line[i + 1].Value, out var inherited):
                vars[name] = inherited;
                break;
            default:
                vars.Remove(name);
                break;
        }
    }

    private static string? Resolve(Arg arg, Dictionary<string, string> vars)
    {
        if (arg.VarName is not null)
            return vars.TryGetValue(arg.VarName, out var v) ? v : null;
        if (arg.IsWord && vars.TryGetValue(arg.Text, out var bare))
            return bare;
        return arg.Text.Contains('$') ? null : arg.Text;
    }

    private static List<Arg> Arguments(List<Token> line)
    {
        var raw = new List<Arg>();
        for (int i = 1; i < line.Count; i++)
        {
            var t = line[i];
            if (t.Type == TokenType.Comment) break;

            if (t.Type == TokenType.Dollar)
            {
                if (i + 1 < line.Count && line[i + 1].Type == TokenType.Identifier)
                {
                    string name = line[i + 1].Value;
                    raw.Add(new Arg("$" + name, name, false, t.Column, name.Length + 1, true));
                    i++;
                }
                else raw.Add(new Arg("$", null, false, t.Column, 1, true));
                continue;
            }

            if (t.Type is TokenType.Command or TokenType.Identifier)
                raw.Add(new Arg(t.Value, null, true, t.Column, t.Value.Length, true));
            else if (t.Type == TokenType.Number)
                raw.Add(new Arg(t.Value, null, false, t.Column, t.Value.Length, true));
            else if (t.Type == TokenType.String)
                raw.Add(new Arg(t.Value, null, false, t.Column, t.Value.Length + 2, false));
        }

        if (raw.Count < 2) return raw;

        var merged = new List<Arg>(raw.Count);
        var current = raw[0];
        for (int i = 1; i < raw.Count; i++)
        {
            var next = raw[i];
            if (current.Mergeable && next.Mergeable && next.Column == current.Column + current.Length)
                current = new Arg(current.Text + next.Text, null, false, current.Column,
                    current.Length + next.Length, true);
            else
            {
                merged.Add(current);
                current = next;
            }
        }
        merged.Add(current);
        return merged;
    }

    private static List<int> InertCommentLinesWithSemicolon(string script)
    {
        var result = new List<int>();
        string[] lines = script.Replace("\r\n", "\n").Split(['\r', '\n'], StringSplitOptions.None);
        for (int i = 0; i < lines.Length; i++)
        {
            int start = CommentStart(lines[i]);
            if (start >= 0 && lines[i].IndexOf(';', start) >= 0)
                result.Add(i + 1);
        }
        return result;
    }

    private static int CommentStart(string line)
    {
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
                return i;
        }
        return -1;
    }
}
