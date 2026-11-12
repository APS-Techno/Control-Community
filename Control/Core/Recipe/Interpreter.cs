using System.Globalization;
using APS.RecipeModel.Validation;

namespace APS.SystemControl.Core.Recipe;

/// <summary>
/// Processes tokenized recipe lines, handling variable declaration/substitution,
/// loop expansion, and function definition/invocation.
///
/// The Interpreter is stateful: variables and functions persist across calls
/// within a single recipe execution. Call <see cref="Reset"/> between recipes.
/// </summary>
public class Interpreter
{
    private readonly Dictionary<string, RecipeVariable> _variables = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Block context produced by the most recent <see cref="BuildBlockStructure"/> call.
    /// Used by the legacy <see cref="GetBlock"/>/<see cref="GetLabelLine"/>/<see cref="Labels"/>
    /// accessors. Nested 'call' frames pass their own BlockContext explicitly through
    /// ExecuteTokenLines and do not consult this field.
    /// </summary>
    private BlockContext _lastBlockContext = new();

    /// <summary>
    /// Current depth of nested 'call' frames. Bumped by EnterCall, decremented by ExitCall.
    /// Used to prevent runaway recursion. Cleared by Reset().
    /// </summary>
    private int _callDepth;

    /// <summary>
    /// Optional callback to resolve system variables ($pressure, $temperature, etc.)
    /// that are not user-declared. Return null if the variable is unknown.
    /// </summary>
    public Func<string, object?>? SystemVariableProvider { get; set; }

    /// <summary>
    /// Callback to read the current value of a sensor by its ID.
    /// Must be set before executing recipes with 'if' conditions.
    /// Returns the current sensor reading as a float.
    /// </summary>
    public Func<string, float>? SensorValueProvider { get; set; }

    /// <summary>
    /// Callback to read the current state of a device by its ID.
    /// Returns a string like "open", "closed", "on", "off".
    /// Must be set before executing recipes with device-state 'if' conditions.
    /// </summary>
    public Func<string, string>? DeviceStateProvider { get; set; }

    /// <summary>
    /// Resolves an embedded-recipe name to its raw lines. Returns null if unknown.
    /// CoreThread wires this to core.embRecipes during ExecuteRecipeWithInterpreter
    /// setup; tests can inject a dictionary-backed lambda directly.
    /// </summary>
    public Func<string, string[]?>? EmbeddedRecipeResolver { get; set; }

    /// <summary>
    /// Resets all interpreter state (variables, block structure, call depth).
    /// Functions live on <see cref="BlockContext"/> and are reset implicitly when
    /// <see cref="BuildBlockStructure"/> produces a new context.
    /// </summary>
    public void Reset()
    {
        _variables.Clear();
        _lastBlockContext = new BlockContext();
        _callDepth = 0;
    }

    /// <summary>
    /// Maximum nesting depth for block constructs (loop, if/else, function).
    /// </summary>
    public const int MaxNestingDepth = 8;

    /// <summary>
    /// Maximum depth of nested 'call' frames. Matches MaxNestingDepth so that
    /// runaway recursion (A -> B -> A) is caught with the same budget as
    /// loop/if nesting.
    /// </summary>
    public const int MaxCallDepth = 8;

    /// <summary>
    /// Pre-scans tokenized lines to find loop/endloop, function/endfunction,
    /// if/else/endif pairs, and label declarations.
    /// Must be called before expanding loops, evaluating conditionals, or jumping to labels.
    /// Supports nested blocks up to <see cref="MaxNestingDepth"/> levels.
    /// </summary>
    public BlockContext BuildBlockStructure(List<List<Token>> tokenLines)
    {
        var ctx = new BlockContext();

        var loopStack = new Stack<int>();
        var funcStack = new Stack<int>();
        // Stack entries: (ifLineIndex, elseLineIndex or -1)
        var ifStack = new Stack<(int ifLine, int elseLine)>();

        for (int i = 0; i < tokenLines.Count; i++)
        {
            var line = tokenLines[i];
            if (line.Count == 0) continue;

            string cmd = line[0].Value.ToLowerInvariant();

            switch (cmd)
            {
                case "loop":
                    CheckNestingDepth(loopStack.Count + ifStack.Count + funcStack.Count, line[0].Line, "loop");
                    loopStack.Push(i);
                    break;

                case "endloop":
                    if (loopStack.Count == 0)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: 'endloop' without matching 'loop'");
                    int loopStart = loopStack.Pop();
                    ctx.Blocks[loopStart] = new BlockInfo(BlockKind.Loop, loopStart, i);
                    ctx.Blocks[i] = new BlockInfo(BlockKind.Loop, loopStart, i);
                    break;

                case "function":
                    funcStack.Push(i);
                    break;

                case "endfunction":
                    if (funcStack.Count == 0)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: 'endfunction' without matching 'function'");
                    int funcStart = funcStack.Pop();
                    var funcLine = tokenLines[funcStart];
                    var funcDef = ParseFunctionHeader(funcLine, funcStart, i);
                    ctx.Functions[funcDef.Name] = funcDef;
                    ctx.Blocks[funcStart] = new BlockInfo(BlockKind.Function, funcStart, i);
                    ctx.Blocks[i] = new BlockInfo(BlockKind.Function, funcStart, i);
                    break;

                case "if":
                    CheckNestingDepth(loopStack.Count + ifStack.Count + funcStack.Count, line[0].Line, "if");
                    ifStack.Push((i, -1));
                    break;

                case "else":
                    if (ifStack.Count == 0)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: 'else' without matching 'if'");
                    var current = ifStack.Pop();
                    if (current.elseLine != -1)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: duplicate 'else' for 'if' at line {tokenLines[current.ifLine][0].Line}");
                    ifStack.Push((current.ifLine, i));
                    break;

                case "endif":
                    if (ifStack.Count == 0)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: 'endif' without matching 'if'");
                    var ifEntry = ifStack.Pop();
                    int elseLine = ifEntry.elseLine;
                    var ifBlock = new BlockInfo(BlockKind.IfElse, ifEntry.ifLine, i, elseLine);
                    ctx.Blocks[ifEntry.ifLine] = ifBlock;
                    ctx.Blocks[i] = ifBlock;
                    if (elseLine != -1)
                        ctx.Blocks[elseLine] = ifBlock;
                    break;

                case "label":
                    if (line.Count < 2)
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: 'label' requires a name");
                    string labelName = line[1].Value;
                    if (ctx.Labels.ContainsKey(labelName))
                        throw new InvalidOperationException(
                            $"Line {line[0].Line}: duplicate label '{labelName}'");
                    ctx.Labels[labelName] = i;
                    break;

                case "goto":
                    // Validated later — just record that goto exists; target checked after full scan
                    break;
            }
        }

        if (loopStack.Count > 0)
        {
            int unmatchedLine = loopStack.Peek();
            throw new InvalidOperationException(
                $"Line {tokenLines[unmatchedLine][0].Line}: 'loop' without matching 'endloop'");
        }

        if (funcStack.Count > 0)
        {
            int unmatchedLine = funcStack.Peek();
            throw new InvalidOperationException(
                $"Line {tokenLines[unmatchedLine][0].Line}: 'function' without matching 'endfunction'");
        }

        if (ifStack.Count > 0)
        {
            int unmatchedLine = ifStack.Peek().ifLine;
            throw new InvalidOperationException(
                $"Line {tokenLines[unmatchedLine][0].Line}: 'if' without matching 'endif'");
        }

        for (int i = 0; i < tokenLines.Count; i++)
        {
            var line = tokenLines[i];
            if (line.Count >= 2 && line[0].Value.Equals("goto", StringComparison.OrdinalIgnoreCase))
            {
                string target = line[1].Value;
                if (!ctx.Labels.ContainsKey(target))
                    throw new InvalidOperationException(
                        $"Line {line[0].Line}: 'goto' target label '{target}' not found");
            }
        }

        _lastBlockContext = ctx;
        return ctx;
    }

    private static void CheckNestingDepth(int currentDepth, int sourceLine, string keyword)
    {
        if (currentDepth >= MaxNestingDepth)
            throw new InvalidOperationException(
                $"Line {sourceLine}: '{keyword}' exceeds maximum nesting depth of {MaxNestingDepth}");
    }

    public void SetVariable(string name, object value)
    {
        if (_variables.TryGetValue(name, out var existing))
        {
            existing.Value = value;
        }
        else
        {
            _variables[name] = new RecipeVariable
            {
                Name = name,
                Value = value,
                DefaultValue = value,
                Type = InferType(value)
            };
        }
    }

    /// <summary>
    /// Gets the current value of a variable, checking user-declared variables first,
    /// then the system variable provider.
    /// </summary>
    public object? GetVariable(string name)
    {
        if (_variables.TryGetValue(name, out var v))
            return v.Value;
        return SystemVariableProvider?.Invoke(name);
    }

    /// <summary>
    /// Sets recipe parameters (from the selector UI), overriding variable defaults.
    /// </summary>
    public void SetParameters(Dictionary<string, object> parameters)
    {
        foreach (var kvp in parameters)
        {
            SetVariable(kvp.Key, kvp.Value);
        }
    }

    public IReadOnlyDictionary<string, RecipeVariable> Variables => _variables;

    /// <summary>
    /// Resolves $variable references in a token line and returns the resulting
    /// string parts (one per logical argument), compatible with the existing
    /// <c>Core.PrepareParts</c> pipeline.
    /// </summary>
    public string[] ResolveVariables(List<Token> tokens)
    {
        var parts = new List<string>();
        int i = 0;

        while (i < tokens.Count)
        {
            var tok = tokens[i];

            switch (tok.Type)
            {
                case TokenType.Comment:
                    i = tokens.Count;
                    break;

                case TokenType.Dollar:
                    if (i + 1 < tokens.Count && tokens[i + 1].Type == TokenType.Identifier)
                    {
                        string varName = tokens[i + 1].Value;
                        object? val = GetVariable(varName);
                        if (val != null)
                        {
                            parts.Add(FormatValue(val));
                        }
                        else
                        {
                            // Keep the original $name so validators/error messages stay clear
                            parts.Add("$" + varName);
                        }
                        i += 2;
                    }
                    else
                    {
                        parts.Add("$");
                        i++;
                    }
                    break;

                case TokenType.Command:
                case TokenType.Number:
                case TokenType.String:
                    parts.Add(tok.Value);
                    i++;
                    break;

                case TokenType.Identifier:
                    // Auto-resolve bare identifiers that match known variables
                    // (e.g., .rcpx parameters used without $ prefix)
                    object? bareVal = GetVariable(tok.Value);
                    parts.Add(bareVal != null ? FormatValue(bareVal) : tok.Value);
                    i++;
                    break;

                case TokenType.Equals:
                case TokenType.Comma:
                case TokenType.OpenParen:
                case TokenType.CloseParen:
                case TokenType.Colon:
                    // Punctuation tokens are skipped during resolution
                    // (used by var declarations, function headers, etc.)
                    i++;
                    break;

                default:
                    i++;
                    break;
            }
        }

        return [.. parts];
    }

    /// <summary>
    /// Processes a 'var' declaration line: <c>var name = value</c>.
    /// </summary>
    public void ProcessVarDeclaration(List<Token> tokens)
    {
        // Expected form: Command("var") Identifier("name") Equals("=") (Number|Identifier|String)("value")
        // The Equals sign is optional for backward compat: var name value
        if (tokens.Count < 3)
            throw new InvalidOperationException(
                $"Line {tokens[0].Line}: 'var' requires at least a name and value");

        string name = tokens[1].Value;

        int valueIdx = 2;
        if (valueIdx < tokens.Count && tokens[valueIdx].Type == TokenType.Equals)
            valueIdx++;

        if (valueIdx >= tokens.Count)
            throw new InvalidOperationException(
                $"Line {tokens[0].Line}: 'var' declaration missing value");

        var valueTok = tokens[valueIdx];
        object value = valueTok.Type switch
        {
            TokenType.Number => ParseNumber(valueTok.Value),
            TokenType.String => valueTok.Value,
            TokenType.Identifier when valueTok.Value.Equals("true", StringComparison.OrdinalIgnoreCase) => true,
            TokenType.Identifier when valueTok.Value.Equals("false", StringComparison.OrdinalIgnoreCase) => false,
            _ => valueTok.Value
        };

        SetVariable(name, value);
    }

    /// <summary>
    /// Gets the block info for a line index in the most recently built structure,
    /// or null if it's not a block boundary. Nested 'call' frames consult their
    /// own BlockContext directly and do not use this accessor.
    /// </summary>
    public BlockInfo? GetBlock(int lineIndex) => _lastBlockContext.GetBlock(lineIndex);

    /// <summary>
    /// Gets the line index for a named label in the most recently built structure,
    /// or -1 if not found.
    /// </summary>
    public int GetLabelLine(string labelName) => _lastBlockContext.GetLabelLine(labelName);

    public IReadOnlyDictionary<string, int> Labels => _lastBlockContext.Labels;

    public IEnumerable<string[]> ExpandLoop(List<List<Token>> tokenLines, int startLine, int endLine, int count)
    {
        for (int iteration = 0; iteration < count; iteration++)
        {
            // Set built-in loop counter variable
            SetVariable("_i", iteration);

            for (int lineIdx = startLine + 1; lineIdx < endLine; lineIdx++)
            {
                var line = tokenLines[lineIdx];
                if (line.Count == 0) continue;

                string cmd = line[0].Value.ToLowerInvariant();

                // Skip nested loop/endloop boundaries — they'll be handled recursively
                if (cmd == "loop" || cmd == "endloop")
                    continue;
                if (cmd == "function" || cmd == "endfunction")
                    continue;

                yield return ResolveVariables(line);
            }
        }
    }

    public int GetLoopCount(List<Token> loopLineTokens)
    {
        // Expected: Command("loop") Number("5") or Command("loop") Dollar Identifier("count")
        if (loopLineTokens.Count < 2)
            throw new InvalidOperationException(
                $"Line {loopLineTokens[0].Line}: 'loop' requires a count argument");

        int idx = 1;
        if (loopLineTokens[idx].Type == TokenType.Number)
        {
            return (int)Math.Round(double.Parse(loopLineTokens[idx].Value, CultureInfo.InvariantCulture));
        }
        else if (loopLineTokens[idx].Type == TokenType.Dollar && idx + 1 < loopLineTokens.Count)
        {
            string varName = loopLineTokens[idx + 1].Value;
            object? val = GetVariable(varName);
            if (val == null)
                throw new InvalidOperationException(
                    $"Line {loopLineTokens[0].Line}: loop count variable '${varName}' is not defined");
            return Convert.ToInt32(val);
        }
        else if (loopLineTokens[idx].Type == TokenType.Identifier)
        {
            // Bare identifier — same auto-resolution as ResolveVariables applies
            // to .rcpx parameters used without the $ prefix (e.g. "loop N").
            object? val = GetVariable(loopLineTokens[idx].Value);
            if (val == null)
                throw new InvalidOperationException(
                    $"Line {loopLineTokens[0].Line}: loop count '{loopLineTokens[idx].Value}' is not a number or a defined variable");
            return Convert.ToInt32(val);
        }
        else
        {
            throw new InvalidOperationException(
                $"Line {loopLineTokens[0].Line}: invalid loop count '{loopLineTokens[idx].Value}'");
        }
    }

    /// <summary>
    /// Checks if a named function exists in the most recently built block context.
    /// Nested 'call' frames consult their own BlockContext directly via blocks.HasFunction.
    /// </summary>
    public bool HasFunction(string name) => _lastBlockContext.HasFunction(name);

    public FunctionDef? GetFunction(string name) => _lastBlockContext.GetFunction(name);

    /// <summary>
    /// Evaluates the condition on an 'if' line.
    /// Supported forms:
    ///   if sensor_id &lt; value
    ///   if sensor_id &gt; value
    ///   if sensor_id = value
    ///   if sensor_id &lt;= value
    ///   if sensor_id &gt;= value
    ///   if $variable &lt; value
    /// The sensor_id is resolved via <see cref="SensorValueProvider"/>;
    /// variable references are resolved via <see cref="GetVariable"/>.
    /// </summary>
    public bool EvaluateIfCondition(List<Token> ifLineTokens)
    {
        // Expected: Command("if") Identifier("sensor_id") Identifier("<") Number("0.05")
        // Or:       Command("if") Dollar Identifier("varName") Identifier("<") Number("0.05")
        if (ifLineTokens.Count < 4)
            throw new InvalidOperationException(
                $"Line {ifLineTokens[0].Line}: 'if' requires at least 3 arguments: sensor/variable condition value");

        int idx = 1;

        float lhsValue;
        if (ifLineTokens[idx].Type == TokenType.Dollar && idx + 1 < ifLineTokens.Count)
        {
            string varName = ifLineTokens[idx + 1].Value;
            object? val = GetVariable(varName);
            if (val == null)
                throw new InvalidOperationException(
                    $"Line {ifLineTokens[0].Line}: undefined variable '${varName}' in 'if' condition");
            lhsValue = Convert.ToSingle(val, CultureInfo.InvariantCulture);
            idx += 2;
        }
        else
        {
            string sensorId = ifLineTokens[idx].Value;
            if (SensorValueProvider == null)
                throw new InvalidOperationException(
                    $"Line {ifLineTokens[0].Line}: cannot evaluate 'if' condition — no sensor value provider configured");
            lhsValue = SensorValueProvider(sensorId);
            idx++;
        }

        if (idx >= ifLineTokens.Count)
            throw new InvalidOperationException(
                $"Line {ifLineTokens[0].Line}: 'if' condition missing operator");
        string op = ifLineTokens[idx].Value;
        idx++;

        // Handle two-character operators tokenized as separate tokens (e.g., "<" "=" -> "<=")
        if (idx < ifLineTokens.Count &&
            (op == "<" || op == ">" || op == "!") &&
            ifLineTokens[idx].Value == "=")
        {
            op += "=";
            idx++;
        }

        if (idx >= ifLineTokens.Count)
            throw new InvalidOperationException(
                $"Line {ifLineTokens[0].Line}: 'if' condition missing comparison value");

        float rhsValue;
        if (ifLineTokens[idx].Type == TokenType.Dollar && idx + 1 < ifLineTokens.Count)
        {
            string varName = ifLineTokens[idx + 1].Value;
            object? val = GetVariable(varName);
            if (val == null)
                throw new InvalidOperationException(
                    $"Line {ifLineTokens[0].Line}: undefined variable '${varName}' in 'if' condition");
            rhsValue = Convert.ToSingle(val, CultureInfo.InvariantCulture);
        }
        else if (ifLineTokens[idx].Type == TokenType.Number)
        {
            rhsValue = ParseNumberLiteral(ifLineTokens[idx].Value);
        }
        else
        {
            string rhsStr = ifLineTokens[idx].Value.ToLowerInvariant();
            rhsValue = rhsStr switch
            {
                "true" or "open" or "on" => 1f,
                "false" or "closed" or "off" => 0f,
                _ => throw new InvalidOperationException(
                    $"Line {ifLineTokens[0].Line}: invalid comparison value '{ifLineTokens[idx].Value}' in 'if' condition")
            };
        }

        return EvaluateComparison(lhsValue, op, rhsValue, ifLineTokens[0].Line);
    }

    internal static bool EvaluateComparison(float lhs, string op, float rhs, int sourceLine)
    {
        // float.Epsilon (~1.4e-45) is effectively never reached by sensor-scale
        // values; use a relative tolerance that scales with the operand.
        static float Tol(float a, float b)
        {
            float scale = Math.Max(Math.Abs(a), Math.Abs(b));
            return Math.Max(1e-6f, scale * 1e-5f);
        }

        return op switch
        {
            "<" => lhs < rhs,
            ">" => lhs > rhs,
            "=" or "==" => Math.Abs(lhs - rhs) <= Tol(lhs, rhs),
            "<=" => lhs <= rhs,
            ">=" => lhs >= rhs,
            "!=" => Math.Abs(lhs - rhs) > Tol(lhs, rhs),
            _ => throw new InvalidOperationException(
                $"Line {sourceLine}: unknown operator '{op}' in 'if' condition")
        };
    }

    public string[]? ResolveEmbeddedRecipe(string name) => EmbeddedRecipeResolver?.Invoke(name);

    /// <summary>
    /// Parses a numeric literal that may carry a pressure-unit suffix. The lexer
    /// deliberately folds '50mT' into ONE Number token so literals round-trip.
    /// Suffixed values are returned in Torr, matching the sensors' canonical unit.
    /// (code audit 2026-08-29, C17)
    /// </summary>
    internal static float ParseNumberLiteral(string value) =>
        float.TryParse(value, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f
            : APS.SystemControl.Core.Classes.PressureParser.ParseTorr(value);

    public void EnterCall(int sourceLine, string targetName)
    {
        if (_callDepth >= MaxCallDepth)
            throw new InvalidOperationException(
                $"Line {sourceLine}: 'call {targetName}' exceeds maximum call depth of {MaxCallDepth} — possible recursion?");
        _callDepth++;
    }

    /// <summary>
    /// Records exit from a 'call' frame. Always paired with <see cref="EnterCall"/> via try/finally.
    /// </summary>
    public void ExitCall() => _callDepth--;

    public static bool IsInterpreterKeyword(string command)
    {
        return command.ToLowerInvariant() switch
        {
            "var" => true,
            "loop" => true,
            "endloop" => true,
            "function" => true,
            "endfunction" => true,
            "if" => true,
            "else" => true,
            "endif" => true,
            "label" => true,
            "goto" => true,
            "begin" => true,
            "end" => true,
            "call" => true,
            _ => false
        };
    }

    private static FunctionDef ParseFunctionHeader(List<Token> headerTokens, int startLine, int endLine)
    {
        // Expected: Command("function") Identifier("name") [OpenParen Identifier [Comma Identifier]... CloseParen]
        if (headerTokens.Count < 2)
            throw new InvalidOperationException(
                $"Line {headerTokens[0].Line}: 'function' requires a name");

        string name = headerTokens[1].Value;
        var paramNames = new List<string>();

        int i = 2;
        if (i < headerTokens.Count && headerTokens[i].Type == TokenType.OpenParen)
        {
            i++;
            while (i < headerTokens.Count && headerTokens[i].Type != TokenType.CloseParen)
            {
                if (headerTokens[i].Type == TokenType.Identifier)
                    paramNames.Add(headerTokens[i].Value);
                i++;
            }
        }

        return new FunctionDef(name, startLine, endLine, paramNames);
    }

    private static object ParseNumber(string s)
    {
        if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return intVal;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double dblVal))
            return dblVal;
        return s;
    }

    private static string InferType(object value) => value switch
    {
        int => "int",
        float => "float",
        double => "float",
        bool => "bool",
        string => "string",
        _ => "string"
    };

    private static string FormatValue(object value) => value switch
    {
        float f => f.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => value.ToString() ?? ""
    };
}

/// <summary>
/// Per-token-list bookkeeping of block boundaries (loops, ifs, functions),
/// label declarations, and function definitions. Each call to
/// <see cref="Interpreter.BuildBlockStructure"/> produces a new instance,
/// so nested 'call' frames each get their own context without trampling
/// the caller's bookkeeping.
///
/// Functions live here (not on Interpreter) because their StartLine/EndLine
/// indices are only meaningful within the token list they were parsed from.
/// </summary>
public class BlockContext
{
    public Dictionary<int, BlockInfo> Blocks { get; } = new();
    public Dictionary<string, int> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, FunctionDef> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public BlockInfo? GetBlock(int lineIndex) =>
        Blocks.TryGetValue(lineIndex, out var b) ? b : null;

    public int GetLabelLine(string name) =>
        Labels.TryGetValue(name, out var l) ? l : -1;

    public bool HasFunction(string name) => Functions.ContainsKey(name);

    public FunctionDef? GetFunction(string name) =>
        Functions.TryGetValue(name, out var f) ? f : null;
}

public enum BlockKind { Loop, Function, IfElse }

/// <summary>
/// Describes a block's boundaries in the tokenized line list (0-based indices).
/// For if/else/endif blocks, ElseLine is the index of the 'else' line (or -1 if no else).
/// </summary>
public record BlockInfo(BlockKind Kind, int StartLine, int EndLine, int ElseLine = -1);

public record FunctionDef(string Name, int StartLine, int EndLine, List<string> ParamNames);
