namespace APS.RecipeModel.Validation;

/// <summary>
/// Severity of a recipe-validation finding. Errors block recipe execution;
/// warnings are surfaced to the operator but the recipe still runs.
/// </summary>
public enum RecipeErrorSeverity { Error, Warning }

public record RecipeError(int Line, int Column, string Message,
    RecipeErrorSeverity Severity = RecipeErrorSeverity.Error);

public class RecipeValidator
{
    /// <summary>Matches the limit enforced by the runtime interpreter.</summary>
    public const int MaxNestingDepth = 8;

    private readonly HashSet<string> _knownCommands;
    private readonly HashSet<string> _knownDevices;
    private readonly HashSet<string> _knownSourceNames;
    private readonly HashSet<string> _knownEmbeddedRecipes;
    private readonly IReadOnlyDictionary<string, (float Min, float Max)> _deviceBounds;
    private readonly string? _throttleValveSensor;
    private readonly HashSet<string> _knownFaces;

    /// <summary>
    /// Command signatures and per-DeviceType device names, derived from the
    /// same declarations the executor binds against. Null when the caller did
    /// not supply them, in which case the signature-driven phase is skipped and
    /// only the legacy hand-maintained checks run.
    ///
    /// Core ALWAYS supplies this for a real run — see CoreThread.ExecuteRecipe.
    /// Without it a recipe cannot be fully checked, and an empty catalogue is
    /// reported as an error rather than silently approving the recipe.
    /// </summary>
    private readonly RecipeSemantics? _semantics;

    /// <summary>
    /// Absolute safety caps applied to numeric literal arguments when no
    /// device-specific bounds are configured. These are conservative ceilings
    /// intended to catch order-of-magnitude typos (e.g. mfc Ar1 999 instead of
    /// 9.99). Tighten via <see cref="RecipeValidator"/>'s deviceBounds parameter
    /// when device-specific min/max become available.
    /// </summary>
    private static readonly Dictionary<string, float> DefaultCommandCeilings = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mfc"]     = 1000f,    // sccm
        ["power"]   = 10000f,   // watts
        ["ramp"]    = 10000f,   // watts
        ["bias"]    = 2000f,    // volts
        ["stage"]   = 1_000_000f, // motor steps
    };

    /// <summary>
    /// Commands that are handled by the interpreter itself and should not be
    /// validated as hardware commands.
    /// </summary>
    public static readonly HashSet<string> InterpreterKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "var", "loop", "endloop", "function", "endfunction",
        "if", "else", "endif", "label", "goto",
        "begin", "end", "delay", "wait", "pwait", "swait",
        "check", "rotation", "call", "state"
    };

    /// <summary>
    /// Hardware and recipe commands recognised by the runtime but not handled
    /// by the interpreter's structural pass (i.e. they are forwarded to Core).
    /// </summary>
    public static readonly HashSet<string> HardwareCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "valve", "mfc", "pump", "stage", "power", "ramp",
        "bias", "biasArb", "set", "warmup",
        "holder", "shutter", "deposit",
        "scan", "scanR", "rotate", "message",
        "start", "finish",
        "isolate",
        "transfer", "home", "abort"
    };

    /// <summary>Pass this to the constructor when full validation is desired.</summary>
    public static readonly HashSet<string> AllKnownCommands =
        new(InterpreterKeywords.Concat(HardwareCommands), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Expected minimum argument counts for built-in commands.
    /// </summary>
    private static readonly Dictionary<string, int> BuiltInArgCounts = new(StringComparer.OrdinalIgnoreCase)
    {
        ["var"] = 2,        // var name value (or var name = value)
        ["loop"] = 1,       // loop N
        ["endloop"] = 0,
        ["function"] = 1,   // function name(...)
        ["endfunction"] = 0,
        ["if"] = 3,         // if sensor condition value
        ["else"] = 0,
        ["endif"] = 0,
        ["label"] = 1,      // label name
        ["goto"] = 1,       // goto name
        ["call"] = 1,       // call NAME
        ["delay"] = 1,      // delay seconds
        ["wait"] = 1,       // wait seconds
        ["pwait"] = 3,      // pwait sensor target timeout
        ["swait"] = 3,      // swait sensor target timeout (passive)
        ["state"] = 1,      // state startup|idle|processing
        ["check"] = 3,      // check sensor condition value
        ["rotation"] = 1,   // rotation on/off [speed]
        ["begin"] = 0,
        ["end"] = 0,
        ["start"] = 0,
        ["finish"] = 0,
        ["valve"] = 2,      // valve ID open|close
        ["mfc"] = 2,        // mfc ID value
        ["pump"] = 2,       // pump ID on|off
        ["stage"] = 2,      // stage ID position
        ["power"] = 2,      // power ID on|off [value]
        ["ramp"] = 2,       // ramp ID value
        ["bias"] = 1,       // bias value
        ["biasArb"] = 4,    // biasArb ch voltage freq duty [phase]
        ["set"] = 3,        // set ID prop value
        ["warmup"] = 1,     // warmup time [sources...]
        ["holder"] = 1,     // holder position
        ["shutter"] = 1,    // shutter position
        ["deposit"] = 2,    // deposit pos time [retract]
        ["scan"] = 4,       // scan ID start end speed
        ["scanR"] = 5,      // scanR ID start end speed count
        ["rotate"] = 1,     // rotate speed
        ["message"] = 1,    // message "text"
        ["isolate"] = 0,    // isolate (no args — closes all valves and zeroes all MFCs)
        ["transfer"] = 2,   // transfer src dst
        ["home"] = 0,
        ["abort"] = 0,
    };

    public RecipeValidator(IEnumerable<string> knownCommands, IEnumerable<string> knownDevices,
        IEnumerable<string>? knownSourceNames = null,
        IEnumerable<string>? knownEmbeddedRecipes = null,
        IReadOnlyDictionary<string, (float Min, float Max)>? deviceBounds = null,
        string? throttleValveSensor = null,
        IEnumerable<string>? knownFaces = null,
        RecipeSemantics? semantics = null)
    {
        _semantics = semantics;
        _knownCommands = new HashSet<string>(knownCommands, StringComparer.OrdinalIgnoreCase);
        _knownDevices = new HashSet<string>(knownDevices, StringComparer.OrdinalIgnoreCase);
        _knownSourceNames = knownSourceNames != null
            ? new HashSet<string>(knownSourceNames, StringComparer.OrdinalIgnoreCase)
            : [];
        _knownEmbeddedRecipes = knownEmbeddedRecipes != null
            ? new HashSet<string>(knownEmbeddedRecipes, StringComparer.OrdinalIgnoreCase)
            : [];
        _deviceBounds = deviceBounds != null
            ? new Dictionary<string, (float, float)>(deviceBounds, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, (float, float)>(StringComparer.OrdinalIgnoreCase);
        _throttleValveSensor = string.IsNullOrWhiteSpace(throttleValveSensor) ? null : throttleValveSensor;
        _knownFaces = knownFaces != null
            ? new HashSet<string>(knownFaces, StringComparer.OrdinalIgnoreCase)
            : [];
        if (_knownFaces.Count > 0)
        {
            _knownFaces.Add("park");   // always-valid synonyms for face 0
            _knownFaces.Add("Home");
        }
    }

    public List<RecipeError> Validate(string recipeText)
    {
        var errors = new List<RecipeError>();

        if (string.IsNullOrWhiteSpace(recipeText))
            return errors;

        var lexer = new Lexer();
        List<List<Token>> tokenLines;
        try
        {
            tokenLines = lexer.Tokenize(recipeText);
        }
        catch (Exception ex)
        {
            errors.Add(new RecipeError(0, 0, $"Tokenization failed: {ex.Message}"));
            return errors;
        }

        // Promote lexer-level diagnostics (stray characters the tokenizer had
        // to skip) to real errors so operators see them instead of silently
        // running on the mutilated token stream (e.g. `stage HS -283` losing its minus sign).
        foreach (var d in lexer.Diagnostics)
            errors.Add(new RecipeError(d.Line, d.Column,
                $"Unexpected character '{d.Character}' — not part of any recipe token"));

        if (tokenLines.Count == 0)
            return errors;

        ValidateStructure(tokenLines, errors);

        ValidateCommands(tokenLines, errors);

        ValidateVariables(tokenLines, errors);

        ValidateDevices(tokenLines, errors);

        ValidateMaterials(tokenLines, errors);

        ValidateRobotFaces(tokenLines, errors);

        ValidateSetpoints(tokenLines, errors);

        ValidateArguments(tokenLines, errors);

        ValidateProcess(tokenLines, errors);

        // Region marker balance (non-fatal). Regions are comments; unbalanced markers
        // do not break execution but are worth flagging in the editor.
        var lines = recipeText.Replace("\r\n", "\n").Split('\n');
        int regionDepth = 0;
        var openLines = new Stack<int>();
        for (int ln = 0; ln < lines.Length; ln++)
        {
            int hash = CommentStart(lines[ln]);
            if (hash < 0) continue;
            string commentPart = lines[ln][hash..];
            if (APS.RecipeModel.Blocks.RegionMarker.TryParseOpen(commentPart) is not null)
            { regionDepth++; openLines.Push(ln + 1); }
            else if (APS.RecipeModel.Blocks.RegionMarker.IsClose(commentPart))
            {
                if (regionDepth == 0)
                    errors.Add(new RecipeError(ln + 1, hash + 1, "Stray #endregion with no matching #region", RecipeErrorSeverity.Warning));
                else { regionDepth--; openLines.Pop(); }
            }
        }
        while (openLines.Count > 0)
            errors.Add(new RecipeError(openLines.Pop(), 1, "Unclosed #region (missing #endregion)", RecipeErrorSeverity.Warning));

        return errors;
    }

    /// <summary>
    /// Finds the index of the comment-start marker (<c>#</c> or <c>//</c>) on a line,
    /// skipping over quoted string spans so a <c>#</c> inside a string literal (e.g.
    /// <c>message "See #region docs"</c>) is not mistaken for a real region marker.
    /// Mirrors the quote-toggle handling in <see cref="Lexer.TokenizeLine"/>. Returns
    /// -1 if the line has no (unquoted) comment.
    /// </summary>
    private static int CommentStart(string line)
    {
        bool inString = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (ch == '#') return i;
            if (ch == '/' && i + 1 < line.Length && line[i + 1] == '/') return i;
        }
        return -1;
    }

    private static void ValidateStructure(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        var loopStack = new Stack<(int lineIdx, int sourceLine)>();
        var funcStack = new Stack<(int lineIdx, int sourceLine)>();
        var ifStack = new Stack<(int lineIdx, int sourceLine, bool hasElse)>();
        var labelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gotoTargets = new List<(int sourceLine, int column, string target)>();

        int totalDepth = 0;

        for (int i = 0; i < tokenLines.Count; i++)
        {
            var line = tokenLines[i];
            if (line.Count == 0) continue;

            string cmd = line[0].Value.ToLowerInvariant();
            int srcLine = line[0].Line;

            switch (cmd)
            {
                case "loop":
                    totalDepth++;
                    if (totalDepth > MaxNestingDepth)
                        errors.Add(new RecipeError(srcLine, line[0].Column,
                            $"Nesting depth exceeds maximum of {MaxNestingDepth}"));
                    loopStack.Push((i, srcLine));
                    break;

                case "endloop":
                    if (loopStack.Count == 0)
                        errors.Add(new RecipeError(srcLine, line[0].Column, "'endloop' without matching 'loop'"));
                    else
                    {
                        loopStack.Pop();
                        totalDepth--;
                    }
                    break;

                case "function":
                    if (loopStack.Count > 0)
                        errors.Add(new RecipeError(srcLine, line[0].Column, "'function' cannot be defined inside a loop"));
                    funcStack.Push((i, srcLine));
                    break;

                case "endfunction":
                    if (funcStack.Count == 0)
                        errors.Add(new RecipeError(srcLine, line[0].Column, "'endfunction' without matching 'function'"));
                    else
                        funcStack.Pop();
                    break;

                case "if":
                    totalDepth++;
                    if (totalDepth > MaxNestingDepth)
                        errors.Add(new RecipeError(srcLine, line[0].Column,
                            $"Nesting depth exceeds maximum of {MaxNestingDepth}"));
                    ifStack.Push((i, srcLine, false));
                    break;

                case "else":
                    if (ifStack.Count == 0)
                        errors.Add(new RecipeError(srcLine, line[0].Column, "'else' without matching 'if'"));
                    else
                    {
                        var top = ifStack.Pop();
                        if (top.hasElse)
                            errors.Add(new RecipeError(srcLine, line[0].Column,
                                $"Duplicate 'else' for 'if' at line {top.sourceLine}"));
                        ifStack.Push((top.lineIdx, top.sourceLine, true));
                    }
                    break;

                case "endif":
                    if (ifStack.Count == 0)
                        errors.Add(new RecipeError(srcLine, line[0].Column, "'endif' without matching 'if'"));
                    else
                    {
                        ifStack.Pop();
                        totalDepth--;
                    }
                    break;

                case "label":
                    if (line.Count >= 2)
                    {
                        string labelName = line[1].Value;
                        if (!labelNames.Add(labelName))
                            errors.Add(new RecipeError(srcLine, line[0].Column,
                                $"Duplicate label '{labelName}'"));
                    }
                    break;

                case "goto":
                    if (line.Count >= 2)
                        gotoTargets.Add((srcLine, line[0].Column, line[1].Value));
                    break;
            }
        }

        foreach (var (_, srcLine, _) in ifStack)
            errors.Add(new RecipeError(srcLine, 1, "'if' without matching 'endif'"));

        foreach (var (_, srcLine) in loopStack)
            errors.Add(new RecipeError(srcLine, 1, "'loop' without matching 'endloop'"));

        foreach (var (_, srcLine) in funcStack)
            errors.Add(new RecipeError(srcLine, 1, "'function' without matching 'endfunction'"));

        foreach (var (srcLine, col, target) in gotoTargets)
        {
            if (!labelNames.Contains(target))
                errors.Add(new RecipeError(srcLine, col, $"'goto' target label '{target}' not found"));
        }
    }

    private void ValidateCommands(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        for (int i = 0; i < tokenLines.Count; i++)
        {
            var line = tokenLines[i];
            if (line.Count == 0) continue;

            string cmd = line[0].Value;
            int srcLine = line[0].Line;
            int srcCol = line[0].Column;

            int argCount = CountArguments(line);

            if (BuiltInArgCounts.TryGetValue(cmd, out int minArgs))
            {
                // When a real signature is available, ValidateArguments checks arity
                // exactly; skip the hand-maintained minimum so the operator
                // doesn't get two messages for one mistake.
                if (argCount < minArgs && _semantics?.Signature(cmd) is null
                    && !(cmd.Equals("pwait", StringComparison.OrdinalIgnoreCase) && _semantics is not null))
                    errors.Add(new RecipeError(srcLine, srcCol,
                        $"'{cmd}' requires at least {minArgs} argument(s), got {argCount}"));

                // Special case: 'call NAME' — verify NAME exists in the registry,
                // but only if a registry was supplied. Otherwise let the runtime catch it.
                if (cmd.Equals("call", StringComparison.OrdinalIgnoreCase)
                    && line.Count >= 2
                    && _knownEmbeddedRecipes.Count > 0)
                {
                    string targetName = line[1].Value;
                    if (!_knownEmbeddedRecipes.Contains(targetName))
                        errors.Add(new RecipeError(srcLine, srcCol,
                            $"unknown embedded recipe '{targetName}'"));
                }

                // 'power ID on|off' is a 2-arg op — a trailing level is silently
                // discarded by the runtime, so the supply energizes at whatever the
                // last 'set' left (often 0 W). This is the #1 sputter-recipe trap:
                // the recipe looks like it asks for 350 W and deposits at nothing.
                if (cmd.Equals("power", StringComparison.OrdinalIgnoreCase)
                    && argCount >= 3
                    && ArgToken(line, 3) is { } extra)
                {
                    string dev = ArgToken(line, 1)?.Value ?? "<psu>";
                    errors.Add(new RecipeError(extra.Line, extra.Column,
                        $"the trailing value '{extra.Value}' is IGNORED — 'power' only switches the " +
                        "output, so the supply energizes at whatever the previous setpoint was " +
                        $"(often 0 W). Set the level first: 'set {dev} Power {extra.Value}'.",
                        RecipeErrorSeverity.Warning));
                }

                // pwait sensor must be the throttle-valve-controlled sensor.
                // A wait on a sensor without a throttle valve (e.g. loadlock pl
                // pumped by SP2) is swait, not pwait — pwait drives the throttle PID
                // and ignores the sensor argument for the convergence check, so
                // "pwait pl 50mT 60" would exit on whatever the main-chamber
                // sensor reads.
                // Multi-chamber configs name the throttle sensor per chamber
                // (C1_pm, C2_pm, ...), so accept both the bare sensor ("pm") and
                // any chamber-prefixed form ("*_pm"); still flags pl / *_pl etc.
                // When a catalogue is available, ValidatePwait owns 'pwait'
                // entirely — its arity and whether a sensor argument is even
                // meaningful depend on the loaded worker. These legacy rules
                // remain for callers that supply no catalogue.
                if (cmd.Equals("pwait", StringComparison.OrdinalIgnoreCase)
                    && _semantics is null
                    && _throttleValveSensor != null
                    && line.Count >= 2
                    && line[1].Type == TokenType.Identifier
                    && !line[1].Value.Equals(_throttleValveSensor, StringComparison.OrdinalIgnoreCase)
                    && !line[1].Value.EndsWith("_" + _throttleValveSensor, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(new RecipeError(line[1].Line, line[1].Column,
                        $"'{cmd} {line[1].Value} ...' is invalid: pwait drives the throttle-valve PID and " +
                        $"only the throttle-valve sensor ('{_throttleValveSensor}') is supported. " +
                        $"Use 'swait {line[1].Value} ...' for a passive sensor wait."));
                }

                // Pressure-unit ambiguity warning: a pwait/swait target without a unit
                // suffix is interpreted as Torr by the runtime — easy to miss when the
                // operator is thinking in mTorr. Skip target=0 (open-TV signal,
                // unit-agnostic) and skip variable substitutions ($name resolves at
                // runtime and may already include a suffix).
                if ((cmd.Equals("swait", StringComparison.OrdinalIgnoreCase)
                     || (cmd.Equals("pwait", StringComparison.OrdinalIgnoreCase) && _semantics is null))
                    && line.Count >= 3
                    && line[2].Type == TokenType.Number)
                {
                    string targetText = line[2].Value;
                    if (!HasPressureSuffix(targetText)
                        && double.TryParse(targetText, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double v)
                        && v != 0.0)
                    {
                        errors.Add(new RecipeError(line[2].Line, line[2].Column,
                            $"bare pressure value '{targetText}' is interpreted as Torr (= {v * 1000:g3} mTorr). " +
                            "Add a unit suffix to disambiguate: '" + targetText + "mT', '" +
                            targetText + "Torr', '" + targetText + "uT', etc.",
                            RecipeErrorSeverity.Warning));
                    }
                }

                continue;
            }

            if (!_knownCommands.Contains(cmd))
                errors.Add(new RecipeError(srcLine, srcCol, $"Unknown command '{cmd}'"));
        }
    }

    private static bool HasPressureSuffix(string text)
    {
        foreach (var suffix in Lexer.PressureSuffixes)
            if (text.EndsWith(suffix, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>
    /// Returns the n-th argument token of a line (n = 1 is the first argument after the
    /// command), applying the same counting rule as <see cref="CountArguments"/>: a
    /// <c>$name</c> reference counts once, as its Identifier; punctuation is skipped and
    /// a comment ends the line. Returns null when the line has fewer arguments.
    /// </summary>
    private static Token? ArgToken(List<Token> line, int n)
    {
        int count = 0;
        for (int i = 1; i < line.Count; i++)
        {
            var t = line[i];
            if (t.Type == TokenType.Comment) break;
            if (t.Type is TokenType.Command or TokenType.Identifier
                       or TokenType.Number or TokenType.String)
            {
                if (++count == n) return t;
            }
        }
        return null;
    }

    private static int CountArguments(List<Token> line)
    {
        int count = 0;
        for (int i = 1; i < line.Count; i++)
        {
            var t = line[i].Type;
            if (t == TokenType.Comment) break;
            if (t == TokenType.Command || t == TokenType.Identifier ||
                t == TokenType.Number || t == TokenType.String)
            {
                count++;
            }
        }
        return count;
    }

    private static void ValidateVariables(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        var declaredVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Only "_i" is provided (Interpreter.ExpandLoop sets it on every iteration).
        // Nothing supplies "pressure" / "temperature" / "time": SystemVariableProvider
        // is never assigned and Core.LoadWorker.LoadVars is an empty stub, so
        // "$pressure" would survive substitution as literal text and fail the float
        // parse downstream. Add names here only alongside a provider that supplies them.
        var systemVars = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "_i"
        };

        foreach (var line in tokenLines)
        {
            if (line.Count >= 2 && line[0].Value.Equals("var", StringComparison.OrdinalIgnoreCase))
                declaredVars.Add(line[1].Value);
        }

        foreach (var line in tokenLines)
        {
            if (line.Count >= 2 && line[0].Value.Equals("function", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 2; i < line.Count; i++)
                {
                    if (line[i].Type == TokenType.Identifier &&
                        line[i - 1].Type != TokenType.Command) // not the function name
                    {
                        declaredVars.Add(line[i].Value);
                    }
                }
            }
        }

        foreach (var line in tokenLines)
        {
            for (int i = 0; i < line.Count; i++)
            {
                if (line[i].Type == TokenType.Dollar && i + 1 < line.Count &&
                    line[i + 1].Type == TokenType.Identifier)
                {
                    string varName = line[i + 1].Value;
                    if (!declaredVars.Contains(varName) && !systemVars.Contains(varName))
                    {
                        errors.Add(new RecipeError(line[i].Line, line[i].Column,
                            $"Undefined variable '${varName}'"));
                    }
                }
            }
        }
    }

    private void ValidateDevices(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        if (_knownDevices.Count == 0)
            return;

        // LEGACY fallback, used only when no RecipeSemantics catalogue was
        // supplied. It is hand-maintained and therefore drifts; ValidateArguments
        // supersedes it by deriving the same information from the executor's own
        // command declarations. Do not rely on this list for new commands.
        var deviceCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "valve", "mfc", "pump", "stage", "sensor", "power", "ramp",
            "set", "scan", "scanR"
        };

        foreach (var line in tokenLines)
        {
            if (line.Count < 2) continue;

            string cmd = line[0].Value;
            if (!deviceCommands.Contains(cmd)) continue;

            // ValidateArguments checks this command against its real signature, with a
            // per-DeviceType lookup and a better message. Don't report twice.
            if (_semantics?.Signature(cmd) is not null) continue;

            var deviceToken = line[1];
            if (deviceToken.Type == TokenType.Dollar)
                continue; // Variable reference — can't resolve statically

            string deviceName = deviceToken.Value;
            if (!_knownDevices.Contains(deviceName))
            {
                errors.Add(new RecipeError(deviceToken.Line, deviceToken.Column,
                    $"Unknown device '{deviceName}' for command '{cmd}'"));
            }
        }
    }

    private void ValidateMaterials(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        if (_knownSourceNames.Count == 0)
            return;

        var materialCommands = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "holder", "shutter", "deposit"
        };

        var literalVars = CollectLiteralVars(tokenLines);

        foreach (var line in tokenLines)
        {
            if (line.Count < 2) continue;

            string cmd = line[0].Value;
            if (!materialCommands.Contains(cmd)) continue;

            if (cmd.Equals("deposit", StringComparison.OrdinalIgnoreCase))
                CheckRetractPosition(line, literalVars, errors);

            var posToken = line[1];
            if (posToken.Type == TokenType.Dollar)
                continue; // Variable reference — can't resolve statically

            string position = posToken.Value;

            // Numeric flange numbers and "Home" are always valid
            if (int.TryParse(position, out _)) continue;
            if (position.Equals("Home", StringComparison.OrdinalIgnoreCase)) continue;

            if (!_knownSourceNames.Contains(position))
            {
                string available = string.Join(", ", _knownSourceNames);
                errors.Add(new RecipeError(posToken.Line, posToken.Column,
                    $"Unknown target '{position}' for '{cmd}'. Installed sources: {available}"));
            }
        }
    }

    private void CheckRetractPosition(List<Token> line, Dictionary<string, Token> literalVars,
        List<RecipeError> errors)
    {
        var args = ArgumentTokens(line);
        if (args.Count < 3 || args[2].IsBareDollar) return;

        string? value = ResolveLiteral(args[2], literalVars);
        if (value is null) return;

        if (value.Equals("Home", StringComparison.OrdinalIgnoreCase)
            || int.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out _)
            || _knownSourceNames.Contains(value))
            return;

        string installed = string.Join(", ", _knownSourceNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        errors.Add(new RecipeError(args[2].Token.Line, args[2].Column,
            $"Unknown retract position '{value}' for 'deposit'. Third argument parks the shutter and " +
            $"must be Home, 0, a flange number, or an installed source: {installed}"));
    }

    private void ValidateProcess(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        var literalVars = CollectLiteralVars(tokenLines);
        var energized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zeroedSetpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentlyOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var everOpened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var flowing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var gateWarned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool supplyTrackingLost = false;
        List<Token>? lastPwait = null;
        var heldPwait = new Dictionary<string, (float Torr, string Literal, string? Sensor)>(
            StringComparer.OrdinalIgnoreCase);

        string? Literal(RecipeArg a) => a.IsBareDollar ? null : ResolveLiteral(a, literalVars);

        void CheckGateValves(Token at)
        {
            if (_semantics is null) return;
            foreach (string mfc in flowing)
            {
                if (!_semantics.MfcGateValves.TryGetValue(mfc, out var gates) || gates.Count == 0) continue;
                if (gates.Any(currentlyOpen.Contains) || !gateWarned.Add(mfc)) continue;
                errors.Add(new RecipeError(at.Line, at.Column,
                    $"mfc '{mfc}' has non-zero flow but none of its inlet gate valves " +
                    $"({string.Join(", ", gates)}) are open", RecipeErrorSeverity.Warning));
            }
        }

        void TrackPwaitTarget(List<RecipeArg> args)
        {
            if (_semantics is null || _semantics.Signature("pwait") is null || args.Count is not (2 or 3)) return;
            string? chamber = PwaitChamber(args, literalVars);
            if (chamber is null) return;

            var target = args[args.Count - 2];
            string? text = target.IsBareDollar ? null : ResolveLiteral(target, literalVars);
            if (text is null || !Parsing.PressureParser.TryParseTorr(text, out float torr) || torr <= 0)
            {
                heldPwait.Remove(chamber);
                return;
            }

            string? sensor = _semantics.BoundDeviceOf("pwait") is null && !args[0].IsBareDollar
                ? ResolveLiteral(args[0], literalVars)
                : null;
            heldPwait[chamber] = (torr, text, sensor);
        }

        void WarnGasOffBeforeThrottleRelease(Token at, string mfc)
        {
            if (_semantics?.ChamberOf(mfc) is not { } chamber
                || !heldPwait.TryGetValue(chamber, out var held)
                || _semantics.LimitsOf(chamber) is not { IdleCeilingTorr: { } ceiling } limits
                || held.Torr <= ceiling
                || flowing.Any(m => string.Equals(_semantics!.ChamberOf(m), chamber, StringComparison.OrdinalIgnoreCase)))
                return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string ceilingText = ceiling.ToString("g6", inv);
            string consequence = limits.IdleDelaySeconds > 0
                ? $"the Idle over-pressure alarm trips if the pressure stays above {ceilingText} Torr for " +
                  $"{limits.IdleDelaySeconds.ToString("g6", inv)} s"
                : "the Idle over-pressure alarm can trip immediately (delay 0 s" +
                  (limits.IdleUnless is null ? "" : $", suppressed only while '{limits.IdleUnless}'") + ")";
            string release = held.Sensor is null ? "pwait 0 1" : $"pwait {held.Sensor} 0 1";

            errors.Add(new RecipeError(at.Line, at.Column,
                $"mfc '{mfc}' 0 stops the last gas flow in chamber '{chamber}', which then enters Idle while " +
                $"the throttle still holds the pwait target '{held.Literal}' (above the {ceilingText} Torr " +
                $"Idle ceiling): {consequence}. Release the throttle while gas still flows, before zeroing " +
                $"the MFCs: '{release}'.", RecipeErrorSeverity.Warning));
        }

        foreach (var line in tokenLines)
        {
            if (line.Count == 0) continue;
            var head = line[0];
            var args = ArgumentTokens(line);

            switch (head.Value.ToLowerInvariant())
            {
                case "message":
                    errors.Add(new RecipeError(head.Line, head.Column,
                        "'message' opens a blocking operator dialog on the Control UI that halts " +
                        "unattended execution and survives abort_recipe", RecipeErrorSeverity.Warning));
                    break;

                case "rotate" when _semantics is { RotationOperational: false } && args.Count >= 1:
                    if (double.TryParse(Literal(args[0]), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double speed) && speed != 0)
                        errors.Add(RotationWarning(head));
                    break;

                case "rotation" when _semantics is { RotationOperational: false } && args.Count >= 1:
                    if (Literal(args[0]) is "on" or "true")
                        errors.Add(RotationWarning(head));
                    break;

                case "valve" when args.Count >= 2:
                    {
                        string? id = Literal(args[0]);
                        string? state = Literal(args[1]);
                        if (id is null || state is null) break;
                        if (state is "open" or "true")
                        {
                            currentlyOpen.Add(id);
                            everOpened.Add(id);
                        }
                        else if (state is "close" or "closed" or "false")
                        {
                            currentlyOpen.Remove(id);
                            if (!everOpened.Contains(id))
                                errors.Add(new RecipeError(args[0].Token.Line, args[0].Column,
                                    $"valve '{id}' is closed but was never opened in this recipe",
                                    RecipeErrorSeverity.Warning));
                        }
                        break;
                    }

                case "isolate":
                    currentlyOpen.Clear();
                    flowing.Clear();
                    heldPwait.Clear();
                    break;

                case "mfc" when args.Count >= 2:
                    {
                        string? id = Literal(args[0]);
                        if (id is null
                            || !double.TryParse(Literal(args[1]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double flow))
                            break;
                        if (flow > 0) flowing.Add(id);
                        else if (flowing.Remove(id)) WarnGasOffBeforeThrottleRelease(head, id);
                        break;
                    }

                case "power" when args.Count >= 2:
                    {
                        string? id = Literal(args[0]);
                        if (id is null) { supplyTrackingLost = true; break; }
                        string? state = Literal(args[1]);
                        if (state is "off" or "false") energized.Remove(id);
                        else energized.Add(id);
                        break;
                    }

                case "set" when args.Count >= 3:
                    {
                        string? psuId = Literal(args[0]);
                        string? paramName = Literal(args[1]);
                        if (psuId is not null
                            && paramName is not null
                            && (paramName.Equals("Current", StringComparison.OrdinalIgnoreCase)
                                || paramName.Equals("Power", StringComparison.OrdinalIgnoreCase))
                            && double.TryParse(Literal(args[2]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double spVal))
                        {
                            if (spVal == 0) zeroedSetpoints.Add(psuId);
                            else if (spVal > 0) zeroedSetpoints.Remove(psuId);
                        }
                        break;
                    }

                case "ramp" when args.Count >= 2:
                    {
                        string? psuId = Literal(args[0]);
                        if (psuId is not null
                            && double.TryParse(Literal(args[1]), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out double rVal)
                            && rVal > 0)
                            zeroedSetpoints.Remove(psuId);
                        break;
                    }

                case "deposit" when args.Count >= 1:
                    if (!supplyTrackingLost
                        && _semantics is not null
                        && Literal(args[0]) is { } source
                        && _semantics.SourceToPsu.TryGetValue(source, out string? psu)
                        && !energized.Contains(psu))
                        errors.Add(new RecipeError(args[0].Token.Line, args[0].Column,
                            $"deposit '{source}' runs while its power supply '{psu}' is not energized " +
                            $"('warmup' does not leave power on; add 'power {psu} on' before depositing)",
                            RecipeErrorSeverity.Warning));
                    if (!supplyTrackingLost
                        && _semantics is not null
                        && Literal(args[0]) is { } srcForZero
                        && _semantics.SourceToPsu.TryGetValue(srcForZero, out string? psuForZero)
                        && zeroedSetpoints.Contains(psuForZero))
                        errors.Add(new RecipeError(args[0].Token.Line, args[0].Column,
                            $"deposit '{srcForZero}' runs while power supply '{psuForZero}' setpoint is 0 " +
                            $"('warmup' is a timed dwell only and does not set Current or Power; add a non-zero 'set {psuForZero} Current/Power <value>' before depositing)",
                            RecipeErrorSeverity.Warning));
                    CheckGateValves(head);
                    break;

                case "swait" when args.Count >= 2:
                    {
                        string? swSensor = Literal(args[0]);
                        string? swTargetText = Literal(args[1]);
                        if (swSensor is not null
                            && swTargetText is not null
                            && Parsing.PressureParser.TryParseTorr(swTargetText, out float swTorr)
                            && swTorr > 0)
                        {
                            if (_semantics is not null && swTorr <= 1e-7f)
                                errors.Add(new RecipeError(args[1].Token.Line, args[1].Column,
                                    $"swait '{swSensor} {swTargetText}' is at or below the 1e-7 Torr (0.0001mT) high-vacuum gauge floor and will time out; " +
                                    "use a target above 1e-7 Torr and below the 1e-4 Torr Idle ceiling (for example '0.05mT' or '1e-3mT')",
                                    RecipeErrorSeverity.Warning));

                            if (_semantics?.ChamberOf(swSensor) is { } swChamber && swTorr <= 1e-4f)
                            {
                                var activeChamberMfcs = flowing
                                    .Where(m => string.Equals(_semantics.ChamberOf(m), swChamber, StringComparison.OrdinalIgnoreCase))
                                    .OrderBy(m => m, StringComparer.OrdinalIgnoreCase)
                                    .ToList();
                                if (activeChamberMfcs.Count > 0)
                                    errors.Add(new RecipeError(head.Line, head.Column,
                                        $"swait '{swSensor} {swTargetText}' waits for high vacuum while gas is still flowing on " +
                                        $"{string.Join(", ", activeChamberMfcs)} in chamber '{swChamber}' — zero the MFCs ('mfc ... 0') " +
                                        "after releasing the throttle ('pwait 0 1') and before 'swait'",
                                        RecipeErrorSeverity.Warning));
                            }
                        }
                        break;
                    }

                case "pwait":
                    lastPwait = line;
                    TrackPwaitTarget(args);
                    CheckGateValves(head);
                    break;
            }
        }

        if (lastPwait is not null && _semantics?.HasBoundDevice("pwait") == true)
            CheckFinalPwaitFloor(lastPwait, literalVars, errors);
    }

    private static RecipeError RotationWarning(Token head) =>
        new(head.Line, head.Column,
            $"substrate rotation ('{head.Value.ToLowerInvariant()}') is not operational on this machine",
            RecipeErrorSeverity.Warning);

    private void CheckFinalPwaitFloor(List<Token> line, Dictionary<string, Token> literalVars,
        List<RecipeError> errors)
    {
        var args = ArgumentTokens(line);
        if (args.Count is not (2 or 3)) return;
        var target = args[args.Count - 2];
        if (target.IsBareDollar) return;

        string? text = ResolveLiteral(target, literalVars);
        if (text is null || !Parsing.PressureParser.TryParseTorr(text, out float torr)) return;

        if (torr > 0 && torr <= _semantics!.GaugeFloorTorr)
            errors.Add(new RecipeError(target.Token.Line, target.Column,
                $"final pwait target '{text}' is at or below the gauge floor (0.01 mTorr): the runtime " +
                "opens the throttle fully and then waits for the gauge to read at or below the floor, so " +
                "the step fails with a timeout if the chamber cannot pump that low; use target 0 " +
                "('pwait 0 1') to open the throttle and continue at once", RecipeErrorSeverity.Warning));
    }

    private void ValidateRobotFaces(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        if (_knownFaces.Count == 0)
            return; // no face catalog (no robot configured) -> skip gracefully

        foreach (var line in tokenLines)
        {
            if (line.Count == 0) continue;
            if (!line[0].Value.Equals("transfer", StringComparison.OrdinalIgnoreCase)) continue;

            int argsSeen = 0;
            int i = 1;
            while (i < line.Count && argsSeen < 2)
            {
                var tok = line[i];
                if (tok.Type == TokenType.Comment) break;
                if (tok.Type == TokenType.Dollar)
                {
                    // $var is Dollar + Identifier (two tokens). A bare $ (lexer
                    // diagnostic) is a lone Dollar — don't swallow the next face.
                    i += (i + 1 < line.Count && line[i + 1].Type == TokenType.Identifier) ? 2 : 1;
                    argsSeen++;
                    continue;
                }
                string face = tok.Value;
                i++;
                argsSeen++;
                if (int.TryParse(face, out _)) continue;
                if (_knownFaces.Contains(face)) continue;

                string available = string.Join(", ", _knownFaces);
                errors.Add(new RecipeError(tok.Line, tok.Column,
                    $"Unknown robot face '{face}'. Known faces: {available}"));
            }
        }
    }

    private void ValidateSetpoints(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        var deviceValueCommands = new Dictionary<string, (int DevIdx, int ValIdx)>(StringComparer.OrdinalIgnoreCase)
        {
            ["mfc"]   = (1, 2),  // mfc ID value
            ["power"] = (1, 2),  // power ID value  (or on/off — skip non-numeric)
            ["ramp"]  = (1, 2),  // ramp ID value
            ["stage"] = (1, 2),  // stage ID position
            ["bias"]  = (-1, 1), // bias value      (no device ID)
        };

        foreach (var line in tokenLines)
        {
            if (line.Count < 2) continue;
            string cmd = line[0].Value;
            if (!deviceValueCommands.TryGetValue(cmd, out var slots)) continue;
            if (line.Count <= slots.ValIdx) continue;

            var valTok = line[slots.ValIdx];
            if (valTok.Type != TokenType.Number) continue; // non-numeric (e.g. "on"/"off") handled elsewhere
            if (!float.TryParse(valTok.Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float value))
                continue; // lexer already flagged malformed numbers

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                errors.Add(new RecipeError(valTok.Line, valTok.Column,
                    $"'{cmd}' setpoint must be a finite number (got '{valTok.Value}')"));
                continue;
            }

            // Negative setpoints are allowed for stage (can park below home)
            // but never for flow, power, bias.
            if (value < 0 && !cmd.Equals("stage", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new RecipeError(valTok.Line, valTok.Column,
                    $"'{cmd}' setpoint must be non-negative (got {value})"));
                continue;
            }

            (float min, float max)? bounds = null;
            if (slots.DevIdx >= 0 && line.Count > slots.DevIdx)
            {
                var devTok = line[slots.DevIdx];
                if (devTok.Type == TokenType.Identifier &&
                    _deviceBounds.TryGetValue(devTok.Value, out var b))
                    bounds = b;
            }

            if (bounds is { } bv)
            {
                if (value < bv.min || value > bv.max)
                    errors.Add(new RecipeError(valTok.Line, valTok.Column,
                        $"'{cmd}' setpoint {value} out of range [{bv.min}, {bv.max}] for device"));
            }
            else if (DefaultCommandCeilings.TryGetValue(cmd, out float ceiling))
            {
                if (value > ceiling)
                    errors.Add(new RecipeError(valTok.Line, valTok.Column,
                        $"'{cmd}' setpoint {value} exceeds conservative ceiling {ceiling}"));
            }
        }
    }

    /// <summary>One argument as the executor will see it after whitespace splitting.</summary>
    /// <param name="Token">First token of the argument, for error positioning.</param>
    private readonly record struct RecipeArg(
        Token Token, string Text, int Column, bool IsVariable, bool IsBareDollar);

    /// <summary>
    /// Collects the arguments of a line the way the executor does.
    /// A <c>$name</c> reference is ONE argument carrying its text; a dangling
    /// <c>$</c> with no name after it is also one argument, flagged, because
    /// that is exactly what reaches the runtime as the literal string "$".
    /// </summary>
    private static List<RecipeArg> ArgumentTokens(List<Token> line)
    {
        var args = new List<RecipeArg>();
        for (int i = 1; i < line.Count; i++)
        {
            var t = line[i];
            if (t.Type == TokenType.Comment) break;

            if (t.Type == TokenType.Dollar)
            {
                if (i + 1 < line.Count && line[i + 1].Type == TokenType.Identifier)
                {
                    args.Add(new RecipeArg(line[i + 1], "$" + line[i + 1].Value, t.Column, true, false));
                    i++;
                }
                else
                {
                    args.Add(new RecipeArg(t, "$", t.Column, false, true));
                }
                continue;
            }

            if (t.Type is TokenType.Command or TokenType.Identifier
                       or TokenType.Number or TokenType.String)
                args.Add(new RecipeArg(t, t.Value, t.Column, false, false));
        }

        return MergeAdjacent(args);
    }

    /// <summary>
    /// Joins tokens that are adjacent in the source into a single argument.
    ///
    /// The validator sees TOKENS; the executor splits the raw line on
    /// WHITESPACE. "30x" is two tokens (Number 30, Identifier x) but one
    /// executor argument, and TimeParser receives the whole thing. Without this
    /// merge, "delay 30x" validates clean and then throws at execution — the
    /// exact failure mode this phase exists to prevent.
    ///
    /// String tokens are never merged: their Value has the quotes stripped, so
    /// column arithmetic across them is meaningless.
    /// </summary>
    private static List<RecipeArg> MergeAdjacent(List<RecipeArg> args)
    {
        if (args.Count < 2) return args;

        var merged = new List<RecipeArg>(args.Count);
        var current = args[0];

        for (int i = 1; i < args.Count; i++)
        {
            var next = args[i];
            bool adjacent =
                next.Token.Line == current.Token.Line
                && next.Column == current.Column + current.Text.Length
                && current.Token.Type != TokenType.String
                && next.Token.Type != TokenType.String;

            if (adjacent)
            {
                // A merged run ("$X" + "mT") is no longer a plain variable
                // reference, so it is type-checked as literal text.
                current = current with { Text = current.Text + next.Text, IsVariable = false };
            }
            else
            {
                merged.Add(current);
                current = next;
            }
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// Collects <c>var NAME VALUE</c> / <c>var NAME = VALUE</c> declarations whose
    /// value is a literal, so a later <c>$NAME</c> can be type-checked against the
    /// value it will actually carry. This is the cheap form of "validate the
    /// expanded program": a variable holding "30s" fails a float slot at
    /// validation time instead of at execution time.
    /// </summary>
    private static Dictionary<string, Token> CollectLiteralVars(List<List<Token>> tokenLines)
    {
        var map = new Dictionary<string, Token>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in tokenLines)
        {
            if (line.Count < 3) continue;
            if (!line[0].Value.Equals("var", StringComparison.OrdinalIgnoreCase)) continue;
            if (line[1].Type != TokenType.Identifier) continue;

            // Value is the first Number/String/Identifier after an optional '='
            for (int i = 2; i < line.Count; i++)
            {
                var t = line[i];
                if (t.Type == TokenType.Comment) break;
                if (t.Type == TokenType.Equals) continue;
                if (t.Type is TokenType.Number or TokenType.String or TokenType.Identifier)
                {
                    map[line[1].Value] = t;
                    break;
                }
                break; // anything else (e.g. a $ref) — not a literal, leave unmapped
            }
        }
        return map;
    }

    private void ValidateArguments(List<List<Token>> tokenLines, List<RecipeError> errors)
    {
        // A dangling '$' is invalid regardless of whether a catalogue was
        // supplied — it can never be anything but the literal text "$".
        foreach (var line in tokenLines)
        {
            foreach (var arg in ArgumentTokens(line))
            {
                if (arg.IsBareDollar)
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        "stray '$' with no variable name — this reaches the runtime as the " +
                        "literal text \"$\" and fails when the argument is parsed"));
            }
        }

        if (_semantics is null)
            return;   // caller supplied no catalogue: legacy checks only

        if (_semantics.IsEmpty)
        {
            // Default-deny. An empty catalogue means the validator cannot prove
            // ANY device reference or argument type is correct, so it must not
            // report the recipe as clean.
            errors.Add(new RecipeError(0, 0,
                "recipe cannot be validated: no command/device catalogue available " +
                "(Worker.xml or HWConf.xml missing or unreadable) — refusing to " +
                "approve a recipe that cannot be checked"));
            return;
        }

        var literalVars = CollectLiteralVars(tokenLines);

        foreach (var line in tokenLines)
        {
            if (line.Count == 0) continue;
            string cmd = line[0].Value;

            // Handled by dedicated phases with their own rules.
            if (cmd.Equals("var", StringComparison.OrdinalIgnoreCase) ||
                cmd.Equals("function", StringComparison.OrdinalIgnoreCase))
                continue;

            var args = ArgumentTokens(line);

            // 'pwait' arity is worker-dependent — handled on its own.
            if (cmd.Equals("pwait", StringComparison.OrdinalIgnoreCase))
            {
                ValidatePwait(line, args, literalVars, errors);
                continue;
            }

            var sig = _semantics.Signature(cmd);
            if (sig is null) continue;   // unknown command already reported by ValidateCommands

            if (args.Count < sig.RequiredArgs)
            {
                string expected = sig.RequiredArgs == sig.Args.Count
                    ? $"{sig.Args.Count} argument(s)"
                    : $"at least {sig.RequiredArgs} argument(s)";
                errors.Add(new RecipeError(line[0].Line, line[0].Column,
                    $"'{cmd}' takes {expected}, got {args.Count}"));
                continue;
            }

            if (!sig.Variadic && args.Count > sig.Args.Count)
            {
                var extra = args[sig.Args.Count];
                errors.Add(new RecipeError(extra.Token.Line, extra.Token.Column,
                    $"'{cmd}' takes {sig.Args.Count} argument(s), got {args.Count} — " +
                    $"'{extra.Text}' is not used by this command and would be silently discarded"));
                continue;
            }

            // Optional trailing slots may be absent — only type-check what the
            // author actually wrote.
            for (int i = 0; i < Math.Min(sig.Args.Count, args.Count); i++)
                CheckArgument(cmd, i, sig.Args[i], args[i], literalVars, errors);

            if (cmd.Equals("set", StringComparison.OrdinalIgnoreCase) && args.Count >= 2)
                CheckSetpointName(args[0], args[1], literalVars, errors);
        }
    }

    /// <summary>
    /// <c>set &lt;device&gt; &lt;param&gt; &lt;value&gt;</c>: the param slot is
    /// free text in Worker.xml, so a misspelt name (`Freq` on a DC supply, `Pulse`
    /// on an RF head) would otherwise fail only when the driver's
    /// ApplySetpoint threw mid-run.
    /// When the catalogue carries the driver's own list for the device, the
    /// name must be on it — compared exactly, because every driver switches on
    /// the exact string. (A ':channel' suffix cannot be written in a
    /// recipe — the lexer makes ':' its own token — so it is not handled here.)
    /// Devices with no list, variable-valued names and unknown devices (already
    /// reported by CheckDeviceArgument) are left alone.
    /// </summary>
    private void CheckSetpointName(RecipeArg deviceArg, RecipeArg paramArg,
        Dictionary<string, Token> literalVars, List<RecipeError> errors)
    {
        if (deviceArg.IsBareDollar || paramArg.IsBareDollar) return;

        string device = ResolveLiteral(deviceArg, literalVars) ?? "";
        string? param = ResolveLiteral(paramArg, literalVars);
        if (param is null) return;   // only known at run time

        var valid = _semantics!.SetpointNames(device);
        if (valid is null) return;   // no list declared for this device

        if (valid.Contains(param, StringComparer.Ordinal)) return;

        errors.Add(new RecipeError(paramArg.Token.Line, paramArg.Token.Column,
            $"'set {device} {param}': unknown parameter '{param}' for {device} — " +
            $"the driver would refuse it at run time. Valid (exact spelling): {string.Join(", ", valid)}"));
    }

    /// <summary>
    /// The literal text an argument carries: the token itself, or the literal
    /// value of the variable it names (either "$name" or a bare identifier
    /// declared with a literal). Null when the value is only known at run time.
    /// </summary>
    private static string? ResolveLiteral(RecipeArg arg, Dictionary<string, Token> literalVars)
    {
        string text = arg.Text;
        string? varName = arg.IsVariable
            ? text[1..]
            : (arg.Token.Type == TokenType.Identifier ? text : null);
        if (varName != null && literalVars.TryGetValue(varName, out var literal))
            return literal.Value;
        return arg.IsVariable ? null : text;
    }

    /// <summary>
    /// Checks a 'pwait' line. Its shape depends on the loaded worker:
    ///
    ///   BOUND pwait (<c>device="Controller#1"</c>) — the controller is
    ///   both the throttle actuator and the gauge. It commands SetPressure on
    ///   that controller and converges on that controller's own reading, so a
    ///   sensor argument cannot select anything. Canonical form:
    ///       pwait &lt;target&gt; &lt;timeout&gt;
    ///   The 3-argument form still executes (the executor drops the inert
    ///   sensor) but earns a warning telling the author to remove it.
    ///
    ///   UNBOUND pwait (cluster) — the sensor id picks WHICH
    ///   chamber's throttle valve to drive, via ThrottleResolver, and which
    ///   gauge to read. It is required:
    ///       pwait &lt;sensor&gt; &lt;target&gt; &lt;timeout&gt;
    /// </summary>
    private void ValidatePwait(List<Token> line, List<RecipeArg> args,
        Dictionary<string, Token> literalVars, List<RecipeError> errors)
    {
        if (_semantics!.Signature("pwait") is null)
        {
            errors.Add(new RecipeError(line[0].Line, line[0].Column,
                "'pwait' is not supported on this machine (no throttle-valve controller is declared in Worker.xml) — " +
                "use 'swait <sensor> <target> <timeout>' for a passive pressure wait or 'delay <seconds>' for gas stabilization"));
            return;
        }

        bool sensorIsInert = _semantics.HasBoundDevice("pwait");
        var head = line[0];

        if (args.Count == 2)
        {
            if (!sensorIsInert)
            {
                errors.Add(new RecipeError(head.Line, head.Column,
                    "'pwait' takes 3 argument(s) on this machine (sensor target timeout) — " +
                    "the sensor selects which chamber's throttle valve to drive"));
                return;
            }

            CheckArgument("pwait", 0, new ArgSpec(ArgKind.Pressure), args[0], literalVars, errors);
            CheckArgument("pwait", 1, new ArgSpec(ArgKind.Time), args[1], literalVars, errors);
            WarnOnBarePressure("pwait", args[0], PwaitChamber(args, literalVars), literalVars, errors);
            return;
        }

        if (args.Count == 3)
        {
            if (sensorIsInert)
            {
                errors.Add(new RecipeError(args[0].Token.Line, args[0].Column,
                    $"'pwait' takes 2 argument(s) on this machine (target timeout) — the throttle " +
                    $"controller is its own gauge, so '{args[0].Text}' is ignored. Remove it: " +
                    $"'pwait {args[1].Text} {args[2].Text}'.",
                    RecipeErrorSeverity.Warning));
            }
            else if (_throttleValveSensor != null
                     && !args[0].IsBareDollar
                     && args[0].Token.Type == TokenType.Identifier
                     && !args[0].Text.Equals(_throttleValveSensor, StringComparison.OrdinalIgnoreCase)
                     && !args[0].Text.EndsWith("_" + _throttleValveSensor, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(new RecipeError(args[0].Token.Line, args[0].Column,
                    $"'pwait {args[0].Text} ...' is invalid: pwait drives a throttle-valve PID and " +
                    $"only a throttle-valve sensor ('{_throttleValveSensor}' or '*_{_throttleValveSensor}') " +
                    $"is supported. Use 'swait {args[0].Text} ...' for a passive sensor wait."));
            }

            CheckArgument("pwait", 1, new ArgSpec(ArgKind.Pressure), args[1], literalVars, errors);
            CheckArgument("pwait", 2, new ArgSpec(ArgKind.Time), args[2], literalVars, errors);
            WarnOnBarePressure("pwait", args[1], PwaitChamber(args, literalVars), literalVars, errors);
            return;
        }

        errors.Add(new RecipeError(head.Line, head.Column,
            $"'pwait' takes {(sensorIsInert ? "2" : "3")} argument(s), got {args.Count}"));
    }

    /// <summary>
    /// A pressure target with no unit suffix is read as Torr, which is easy to
    /// miss when the operator is thinking in mTorr. When the chamber's
    /// Processing over-pressure ceiling is known and the value exceeds it, the
    /// note states that the outcome (alarm trip versus a pwait timeout) depends
    /// on what the chamber physically does, which validation cannot know.
    /// </summary>
    private void WarnOnBarePressure(string cmd, RecipeArg arg, string? chamber,
        Dictionary<string, Token> literalVars, List<RecipeError> errors)
    {
        string text = arg.Text;
        if (arg.IsVariable)
        {
            if (!literalVars.TryGetValue(text[1..], out var lit)) return;
            text = lit.Value;
        }
        else if (arg.Token.Type == TokenType.Identifier
                 && literalVars.TryGetValue(text, out var lit2))
        {
            text = lit2.Value;
        }

        if (HasPressureSuffix(text)) return;
        if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v)) return;
        if (v == 0.0) return;   // the "open the throttle valve" idiom is unit-agnostic

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string message =
            $"bare pressure value '{text}' is interpreted as Torr (= {(v * 1000).ToString("g6", inv)} mTorr). " +
            $"Add a unit suffix to disambiguate: '{text}mT', '{text}Torr', '{text}uT', etc.";

        if (chamber is not null
            && _semantics?.LimitsOf(chamber)?.ProcessingCeilingTorr is { } ceiling
            && v > ceiling)
            message +=
                $" This value is above the {ceiling.ToString("g6", inv)} Torr Processing alarm threshold of " +
                $"chamber '{chamber}'; write '{text}mT' for {text} mTorr. Note: whether the chamber " +
                $"physically exceeds {ceiling.ToString("g6", inv)} Torr and trips the over-pressure alarm, or stays " +
                "below the target until pwait times out, cannot be determined without running the recipe.";

        errors.Add(new RecipeError(arg.Token.Line, arg.Column, message, RecipeErrorSeverity.Warning));
    }

    private string? PwaitChamber(List<RecipeArg> args, Dictionary<string, Token> literalVars)
    {
        if (_semantics is null) return null;
        if (_semantics.BoundDeviceOf("pwait") is { } device)
            return _semantics.ChamberOf(device);
        if (args.Count != 3 || args[0].IsBareDollar) return null;
        return ResolveLiteral(args[0], literalVars) is { } sensor ? _semantics.ChamberOf(sensor) : null;
    }

    private void CheckArgument(string cmd, int index, ArgSpec spec, RecipeArg arg,
        Dictionary<string, Token> literalVars, List<RecipeError> errors)
    {
        if (arg.IsBareDollar) return;   // already reported

        string text = arg.Text;

        // Resolve variable references to the value they will actually carry, so
        // the slot is type-checked against the substituted text.
        //
        // A reference is EITHER "$name" or a bare identifier that matches a
        // declared variable — Interpreter.ResolveTokens (Interpreter.cs:344)
        // resolves a bare Identifier through GetVariable, and .rcpx parameter
        // references are written without the sigil ("set RF601 Power powerBN").
        string? varName = arg.IsVariable
            ? text[1..]
            : (arg.Token.Type == TokenType.Identifier ? text : null);

        if (varName != null && literalVars.TryGetValue(varName, out var literal))
        {
            text = literal.Value;
        }
        else if (arg.IsVariable)
        {
            // Declared but not literal-valued — its value is only known at run
            // time. ValidateVariables has already confirmed the declaration.
            return;
        }

        switch (spec.Kind)
        {
            case ArgKind.Device:
                CheckDeviceArgument(cmd, spec.DeviceKind!, text, arg.Token, errors);
                break;

            case ArgKind.Number:
                if (!float.TryParse(text, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        $"'{cmd}' argument {index + 1} must be a number, got '{text}'" +
                        (LooksLikeSuffixedNumber(text)
                            ? " — unit suffixes are only accepted by delay/wait and the " +
                              "pwait/swait target and timeout; this slot takes a plain number"
                            : "")));
                break;

            case ArgKind.Integer:
                if (!int.TryParse(text, System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        $"'{cmd}' argument {index + 1} must be a whole number, got '{text}'"));
                break;

            case ArgKind.Bool:
                if (!IsBoolLiteral(text))
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        $"'{cmd}' argument {index + 1} must be one of " +
                        $"open/close/closed/on/off/true/false, got '{text}'" +
                        (IsBoolLiteralIgnoringCase(text)
                            ? " — the runtime substitution is case-sensitive, use lower case"
                            : "")));
                break;

            case ArgKind.Time:
                if (!Parsing.TimeParser.TryParseSeconds(text, out _))
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        $"'{cmd}' argument {index + 1} must be a time value " +
                        $"(number with optional s, m or h suffix), got '{text}'"));
                break;

            case ArgKind.Pressure:
                if (!Parsing.PressureParser.TryParseTorr(text, out _))
                    errors.Add(new RecipeError(arg.Token.Line, arg.Token.Column,
                        $"'{cmd}' argument {index + 1} must be a pressure value " +
                        $"(number with optional T/mT/uT/nT/kT suffix), got '{text}'"));
                break;

            case ArgKind.Text:
                break;   // free text; material/face phases check the ones that matter
        }
    }

    private void CheckDeviceArgument(string cmd, string deviceKind, string name,
        Token token, List<RecipeError> errors)
    {
        if (!_semantics!.HasDevicesOfKind(deviceKind))
        {
            errors.Add(new RecipeError(token.Line, token.Column,
                $"'{cmd}' needs a {deviceKind} device but this machine has none configured"));
            return;
        }

        if (_semantics.IsDeviceOfKind(deviceKind, name))
            return;

        // Name the likely mistake: existing device, wrong kind for this command.
        string hint = _semantics.AllDeviceNames.Contains(name)
            ? $" — '{name}' exists but is not a {deviceKind} device"
            : "";

        string available = string.Join(", ", _semantics.DevicesOfKind(deviceKind).OrderBy(d => d));
        errors.Add(new RecipeError(token.Line, token.Column,
            $"Unknown {deviceKind} device '{name}' for command '{cmd}'{hint}. " +
            $"Configured {deviceKind} devices: {available}"));
    }

    /// <summary>Mirrors CoreThread.ApplySubstitutions + bool.TryParse.</summary>
    private static bool IsBoolLiteral(string text) =>
        text is "open" or "close" or "closed" or "on" or "off"
        || bool.TryParse(text, out _);

    private static bool IsBoolLiteralIgnoringCase(string text) =>
        text.Equals("open", StringComparison.OrdinalIgnoreCase)
        || text.Equals("close", StringComparison.OrdinalIgnoreCase)
        || text.Equals("closed", StringComparison.OrdinalIgnoreCase)
        || text.Equals("on", StringComparison.OrdinalIgnoreCase)
        || text.Equals("off", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for things like "30s" or "50mT" — a number with a trailing unit.</summary>
    private static bool LooksLikeSuffixedNumber(string text) =>
        Parsing.TimeParser.TryParseSeconds(text, out _)
        || Parsing.PressureParser.TryParseTorr(text, out _);
}
