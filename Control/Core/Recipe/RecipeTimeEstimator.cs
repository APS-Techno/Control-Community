using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Core.Recipe;

/// <summary>
/// Estimates how much of a running recipe is left, in seconds.
///
/// The model is a duration plan instead of a step count:
///
/// <list type="number">
/// <item>Every line gets the duration the DSL declares for it — <c>delay</c>,
///   <c>deposit</c>, <c>warmup</c>, a pump spin-up exactly; <c>pwait</c> /
///   <c>swait</c> as their timeout, which is an upper bound. Commands that
///   return immediately are zero; anything whose cost cannot be read from the
///   line (a stage move, an operator <c>message</c>) is unknown.</item>
/// <item>A line inside a loop is planned once per iteration, nested loops
///   multiplying, so the plan covers the run rather than the source text.</item>
/// <item>A line's measured duration replaces its nominal one as soon as it has
///   run. From the second iteration of a stack onwards the estimate is built
///   from what this machine actually did — which is what makes the open-ended
///   waits and the unknown moves accurate.</item>
/// </list>
///
/// Remaining is then simply the planned cost of the executions still to come
/// plus the one in flight. It falls as work completes and moves up only when
/// the run itself gets more expensive than planned.
///
/// The class is deliberately free of any runtime dependency — it takes recipe
/// text and step notifications and hands back a number — so the whole model is
/// unit-testable (see CoreLogicTests/RecipeTimeEstimatorTests).
///
/// Its structural scan for <c>loop</c> / <c>endloop</c> is its own, independent
/// of <c>Interpreter.BuildBlockStructure</c>. It is a lenient scan on purpose:
/// a recipe it reads differently than the executor yields a worse estimate,
/// never a failed run.
/// </summary>
public sealed class RecipeTimeEstimator
{
    /// <summary>Commands that complete as fast as the bus allows: a register
    /// write, a state change, a variable. Anything not listed here and not
    /// carrying an explicit duration is treated as unknown and learned by
    /// measurement.</summary>
    private static readonly HashSet<string> Instantaneous = new(StringComparer.OrdinalIgnoreCase)
    {
        // device writes
        "valve", "mfc", "power", "set", "bias", "biasarb", "rotation", "rotate",
        "state", "check", "gate", "throttle", "shutteropen", "shutterclose",
        // structure / bookkeeping — no cost of their own
        "loop", "endloop", "if", "else", "endif", "begin", "end", "start", "finish",
        "var", "label", "goto", "function", "endfunction", "return", "print", "log",
    };

    private readonly string[][] _tokens;
    private readonly double?[] _nominal;
    private readonly int[] _multiplier;

    /// <summary>Iteration counts for loops whose count is a variable, supplied
    /// by the executor when it enters the loop and knows the resolved value.</summary>
    private readonly Dictionary<int, int> _resolvedIterations = [];

    private readonly int[] _executions;
    private readonly double[] _measuredTotal;
    private readonly int[] _measuredCount;

    private int _currentLine = -1;
    private double _currentStart;
    private bool _started;

    public RecipeTimeEstimator(IReadOnlyList<string> lines)
    {
        int n = lines?.Count ?? 0;
        _tokens = new string[n][];
        _nominal = new double?[n];
        _multiplier = new int[n];
        _executions = new int[n];
        _measuredTotal = new double[n];
        _measuredCount = new int[n];

        for (int i = 0; i < n; i++) _tokens[i] = Tokenize(lines![i]);

        SubstituteDeclaredVariables();

        for (int i = 0; i < n; i++) _nominal[i] = NominalSecondsOf(_tokens[i]);

        Replan();
    }

    /// <summary>Planned duration of the whole run, loop iterations included.
    /// Uses measured durations wherever a line has already run.</summary>
    public TimeSpan TotalPlanned
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < _multiplier.Length; i++) sum += _multiplier[i] * Cost(i);
            return TimeSpan.FromSeconds(sum);
        }
    }

    /// <summary>Planned duration not yet completed: the executions still to
    /// come plus the step in flight. Null until the first step is reported —
    /// with no step there is nothing to anchor the estimate on.</summary>
    public TimeSpan? Remaining
    {
        get
        {
            if (!_started) return null;

            double sum = 0;
            for (int i = 0; i < _multiplier.Length; i++)
            {
                int pending = _multiplier[i] - _executions[i];
                if (pending > 0) sum += pending * Cost(i);
            }
            if (_currentLine >= 0) sum += Cost(_currentLine);
            return TimeSpan.FromSeconds(sum);
        }
    }

    /// <summary>
    /// Tells the estimator how many iterations a loop will actually run, for a
    /// <c>loop $stacks</c> whose count is only known once the variable resolves.
    /// Call it when entering the loop; the plan is rebuilt immediately.
    /// </summary>
    /// <param name="loopLineIndex">Index of the <c>loop</c> line itself.</param>
    public void SetLoopIterations(int loopLineIndex, int iterations)
    {
        if (loopLineIndex < 0 || loopLineIndex >= _tokens.Length) return;
        _resolvedIterations[loopLineIndex] = Math.Max(1, iterations);
        Replan();
    }

    /// <summary>
    /// Reports that the executor is about to run <paramref name="lineIndex"/>,
    /// at <paramref name="elapsedSeconds"/> on the recipe clock. Closes the
    /// previous step's measurement and counts this execution.
    /// </summary>
    /// <param name="resolvedLine">The line with its variables substituted, as
    /// the executor is about to run it. The plan is built from recipe text, so
    /// a parameterised dwell (<c>deposit Si $SiTime 1</c>) declares nothing
    /// until this arrives — without it the first stack of a run reads as very
    /// nearly free. A measured duration still wins over it.</param>
    public void OnStepStarted(int lineIndex, double elapsedSeconds, string? resolvedLine = null)
    {
        if (lineIndex < 0 || lineIndex >= _tokens.Length) return;

        if (resolvedLine != null)
        {
            double? resolved = NominalSeconds(resolvedLine);
            if (resolved is not null) _nominal[lineIndex] = resolved;
        }

        if (_currentLine >= 0)
        {
            double measured = elapsedSeconds - _currentStart;
            if (measured >= 0)
            {
                _measuredTotal[_currentLine] += measured;
                _measuredCount[_currentLine]++;
            }
        }

        // Lines the executor stepped over — an `if` branch that was not taken,
        // a block it skipped — will never run this pass. Counting them as done
        // keeps them out of the remainder; without this the estimate never
        // reaches zero on a recipe with branches. A backwards move is a loop
        // wrap, where the lines behind us are genuinely coming round again.
        if (lineIndex > _currentLine)
            for (int j = Math.Max(0, _currentLine) + (_currentLine < 0 ? 0 : 1); j < lineIndex; j++)
                if (_executions[j] < _multiplier[j]) _executions[j]++;

        _executions[lineIndex]++;
        _currentLine = lineIndex;
        _currentStart = elapsedSeconds;
        _started = true;
    }

    /// <summary>Forgets the run: execution counts, measurements and the step in
    /// flight. The plan returns to its nominal durations.</summary>
    public void Reset()
    {
        Array.Clear(_executions);
        Array.Clear(_measuredTotal);
        Array.Clear(_measuredCount);
        _currentLine = -1;
        _currentStart = 0;
        _started = false;
    }

    /// <summary>
    /// Duration a recipe line declares for itself, in seconds: exact for the
    /// dwell commands, the timeout (an upper bound) for the waits, zero for the
    /// commands that return immediately, and null when the line's cost cannot
    /// be read from its text and has to be measured.
    /// </summary>
    internal static double? NominalSeconds(string line) => NominalSecondsOf(Tokenize(line));

    private static double? NominalSecondsOf(string[] t)
    {
        if (t.Length == 0) return 0;   // blank / comment line

        string cmd = t[0];

        // delay 30 | wait 5m
        if (Is(cmd, "delay") || Is(cmd, "wait")) return Seconds(t, 1);
        // warmup 120 [SRC...]
        if (Is(cmd, "warmup")) return Seconds(t, 1);
        // deposit SRC 300 [POS]
        if (Is(cmd, "deposit")) return Seconds(t, 2);
        // pwait [SENSOR] TARGET TIMEOUT / swait SENSOR TARGET TIMEOUT — the
        // timeout is a ceiling, and the real cost is learned on first execution.
        if (Is(cmd, "pwait") || Is(cmd, "swait")) return Seconds(t, t.Length - 1);
        // pump ID on 300 — the spin-up wait (skipped when already running)
        if (Is(cmd, "pump"))
            return t.Length == 4 && (Is(t[2], "on") || Is(t[2], "true")) ? Seconds(t, 3) : 0;

        return Instantaneous.Contains(cmd) ? 0 : null;

        static bool Is(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

        static double? Seconds(string[] t, int index)
            => index > 0 && index < t.Length && TimeParser.TryParseSeconds(t[index], out float s)
                ? s
                : null;
    }

    /// <summary>Cost of one execution of a line: what it measured if it has run,
    /// what it declares if it has not, and zero when it declares nothing.</summary>
    private double Cost(int i)
        => _measuredCount[i] > 0 ? _measuredTotal[i] / _measuredCount[i] : _nominal[i] ?? 0;

    /// <summary>Recomputes how many times each line runs: the product of the
    /// iteration counts of the loops enclosing it.</summary>
    private void Replan()
    {
        var open = new Stack<int>();
        int multiplier = 1;

        for (int i = 0; i < _tokens.Length; i++)
        {
            string cmd = _tokens[i].Length > 0 ? _tokens[i][0] : string.Empty;

            if (cmd.Equals("loop", StringComparison.OrdinalIgnoreCase))
            {
                _multiplier[i] = multiplier;      // the loop line runs once per outer pass
                open.Push(i);
                multiplier *= Iterations(i);
                continue;
            }

            if (cmd.Equals("endloop", StringComparison.OrdinalIgnoreCase) && open.Count > 0)
            {
                multiplier /= Iterations(open.Pop());
                _multiplier[i] = multiplier;
                continue;
            }

            _multiplier[i] = multiplier;
        }
    }

    /// <summary>Iteration count of the loop opened at <paramref name="loopLine"/>:
    /// the resolved value if the executor supplied one, else the literal on the
    /// line, else one — an unresolved <c>loop $stacks</c> is planned as a single
    /// pass rather than guessed at.</summary>
    private int Iterations(int loopLine)
    {
        if (_resolvedIterations.TryGetValue(loopLine, out int resolved)) return Math.Max(1, resolved);

        var t = _tokens[loopLine];
        return t.Length >= 2 && int.TryParse(t[1], out int literal) && literal > 0 ? literal : 1;
    }

    /// <summary>
    /// Substitutes <c>$name</c> from the recipe's own <c>var name value</c>
    /// declarations, so a parameterised dwell or stack count is in the plan
    /// before the run starts. An .rcpx reaches the executor as a generated var
    /// block followed by the script, which is exactly where the operator's
    /// parameter values live.
    ///
    /// First declaration wins — that block is the parameters. A variable
    /// reassigned later in the script keeps its planned value and is corrected
    /// by measurement, and one that is never declared stays unresolved rather
    /// than being guessed at.
    /// </summary>
    private void SubstituteDeclaredVariables()
    {
        Dictionary<string, string>? values = null;

        foreach (var t in _tokens)
        {
            if (t.Length < 3 || !t[0].Equals("var", StringComparison.OrdinalIgnoreCase)) continue;
            values ??= new(StringComparer.OrdinalIgnoreCase);
            values.TryAdd(t[1].TrimStart('$'), t[2]);
        }

        if (values is null) return;

        foreach (var t in _tokens)
            for (int j = 0; j < t.Length; j++)
                if (t[j].StartsWith('$') && values.TryGetValue(t[j][1..], out var v))
                    t[j] = v;
    }

    private static string[] Tokenize(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];

        int comment = line.IndexOf('#');
        if (comment >= 0) line = line[..comment];

        var raw = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // The runtime rebuilds a line from lexer tokens, and the lexer emits `$`
        // as a token of its own — "wait $ dwell". Rejoin it with the name it
        // belongs to so a variable reference is one token here too.
        var merged = new List<string>(raw.Length);
        for (int i = 0; i < raw.Length; i++)
        {
            if (raw[i] == "$" && i + 1 < raw.Length)
            {
                merged.Add("$" + raw[i + 1]);
                i++;
                continue;
            }
            merged.Add(raw[i]);
        }
        return [.. merged];
    }
}
