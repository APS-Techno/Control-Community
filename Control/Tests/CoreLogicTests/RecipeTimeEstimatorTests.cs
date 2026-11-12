using APS.SystemControl.Core.Recipe;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Remaining-time estimation for a running recipe is a duration plan, not
/// "average seconds per executed step × steps left": steps are not equal-cost
/// (a `valve` write is 0.2 s, a `deposit` is 300 s), and a `loop` body runs once
/// per iteration while the reported step index snaps back at each wrap. Each line
/// carries the duration the DSL declares for it, loop bodies are counted once per
/// iteration, and a line's measured duration replaces its nominal one as soon as
/// it has run.
/// </summary>
public class RecipeTimeEstimatorTests
{
    private static RecipeTimeEstimator Make(params string[] lines) => new(lines);

    [Theory]
    [InlineData("delay 30", 30)]
    [InlineData("wait 30", 30)]
    [InlineData("delay 5m", 300)]          // TimeParser suffixes
    [InlineData("deposit Cu 300 Home", 300)]
    [InlineData("warmup 120 Cu", 120)]
    [InlineData("pwait pm 5mT 600", 600)]  // timeout is an upper bound
    [InlineData("pwait 5mT 600", 600)]     // bound (two-argument) form
    [InlineData("swait pl 1mT 90", 90)]
    [InlineData("pump TP on 300", 300)]    // spin-up wait
    [InlineData("valve M1 open", 0)]       // instantaneous command
    [InlineData("mfc Ar1 20", 0)]
    public void NominalSeconds_ReadsTheDurationTheDslDeclares(string line, double expected)
        => Assert.Equal(expected, RecipeTimeEstimator.NominalSeconds(line));

    [Theory]
    [InlineData("transfer LL C1")]         // duration is a mechanical unknown
    [InlineData("message Open the load lock")]
    public void NominalSeconds_UnknownDurationCommands_ReturnNull(string line)
        => Assert.Null(RecipeTimeEstimator.NominalSeconds(line));

    [Fact]
    public void TotalPlanned_FlatRecipe_SumsLineDurations()
    {
        var e = Make("valve M1 open", "delay 30", "deposit Cu 300 Home", "valve M1 close");
        Assert.Equal(330, e.TotalPlanned.TotalSeconds);
    }

    [Fact]
    public void TotalPlanned_CountsALoopBodyOncePerIteration()
    {
        var e = Make(
            "pwait pm 5mT 600",
            "loop 3",
            "delay 10",
            "deposit Cu 100 Home",
            "endloop",
            "valve M1 close");
        Assert.Equal(600 + 3 * 110, e.TotalPlanned.TotalSeconds);
    }

    [Fact]
    public void TotalPlanned_NestedLoops_MultiplyIterations()
    {
        var e = Make("loop 2", "loop 3", "delay 10", "endloop", "endloop");
        Assert.Equal(60, e.TotalPlanned.TotalSeconds);
    }

    [Fact]
    public void SetLoopIterations_ResolvesALoopCountThatIsAVariable()
    {
        var e = Make("loop $stacks", "delay 10", "endloop");
        Assert.Equal(10, e.TotalPlanned.TotalSeconds);   // unresolved: assume one pass

        e.SetLoopIterations(0, 4);
        Assert.Equal(40, e.TotalPlanned.TotalSeconds);
    }

    // An .rcpx run reaches the executor as a generated `var` block followed by
    // the script, so the operator's parameters are in the recipe text itself.
    // Reading them at plan time is what makes the estimate right from the first
    // step instead of from the second stack.
    [Fact]
    public void TotalPlanned_ResolvesVariablesDeclaredByTheRecipesOwnVarBlock()
    {
        var e = Make("var stacks 3", "var dwell 30", "loop $stacks", "wait $dwell", "endloop");
        Assert.Equal(90, e.TotalPlanned.TotalSeconds);
    }

    // CoreThread rebuilds each line from lexer tokens, and the lexer emits `$`
    // as a token of its own (RecipeModel/Validation/Lexer.cs) — so the plan is
    // handed "wait $ dwell", not "wait $dwell". Reading that as a dangling `$`
    // leaves a whole parameterised run unpriced.
    [Fact]
    public void TotalPlanned_ReadsAVariableTheLexerSplitFromItsDollarSign()
    {
        var e = Make("var dwell 30", "loop 2", "wait $ dwell", "endloop");
        Assert.Equal(60, e.TotalPlanned.TotalSeconds);
    }

    [Fact]
    public void TotalPlanned_LeavesAnUndeclaredVariableUnknown()
    {
        var e = Make("loop 2", "wait $dwell", "endloop");
        Assert.Equal(0, e.TotalPlanned.TotalSeconds);
    }

    [Fact]
    public void Remaining_BeforeTheFirstStep_IsNull()
        => Assert.Null(Make("delay 30").Remaining);

    [Fact]
    public void Remaining_CountsWorkNotYetFinished_IncludingTheStepInProgress()
    {
        var e = Make("valve M1 open", "delay 30", "deposit Cu 300 Home", "valve M1 close");

        e.OnStepStarted(0, 0);
        Assert.Equal(330, e.Remaining!.Value.TotalSeconds);   // nothing done yet

        e.OnStepStarted(1, 0.2);                              // the valve took 0.2 s
        Assert.Equal(330, e.Remaining!.Value.TotalSeconds);   // delay + deposit still ahead

        e.OnStepStarted(2, 30.2);
        Assert.Equal(300, e.Remaining!.Value.TotalSeconds);

        e.OnStepStarted(3, 330.2);
        Assert.Equal(0, e.Remaining!.Value.TotalSeconds);
    }

    [Fact]
    public void Remaining_MeasuredDurationReplacesTheNominalForLaterIterations()
    {
        // The swait's 600 s is a timeout, not a duration. Once the first
        // iteration shows it really takes 45 s, the two iterations still to
        // come must be priced at 45 s, not at the timeout.
        var e = Make("loop 3", "swait pm 1mT 600", "deposit Cu 100 Home", "endloop");
        Assert.Equal(3 * 700, e.TotalPlanned.TotalSeconds);

        e.OnStepStarted(1, 0);
        Assert.Equal(2100, e.Remaining!.Value.TotalSeconds);

        e.OnStepStarted(2, 45);
        // 2 more swaits @45 + 2 more deposits @100 + this deposit @100
        Assert.Equal(2 * 45 + 2 * 100 + 100, e.Remaining!.Value.TotalSeconds);
    }

    [Fact]
    public void Remaining_UnknownStep_IsLearnedFromItsFirstExecution()
    {
        var e = Make("loop 2", "transfer LL C1", "delay 10", "endloop");
        Assert.Equal(20, e.TotalPlanned.TotalSeconds);   // transfer contributes nothing yet

        e.OnStepStarted(1, 0);
        Assert.Equal(20, e.Remaining!.Value.TotalSeconds);

        e.OnStepStarted(2, 25);   // the transfer took 25 s
        // the second iteration's transfer is now priced at 25 s
        Assert.Equal(25 + 10 + 10, e.Remaining!.Value.TotalSeconds);
    }

    // Operator recipes parameterise their dwells (`deposit Si $SiTime 1`), and the
    // plan is built from the recipe text, where $SiTime is still a variable. The
    // executor resolves it a moment before the step runs and hands the resolved
    // line over — without that, a parameterised dwell is invisible to the plan
    // until it has run once, which on a 4-stack run means the whole first stack
    // is estimated as nearly free.
    [Fact]
    public void Remaining_ResolvedLine_PricesAVariableDwellAsSoonAsItStarts()
    {
        var e = Make("loop 2", "wait $dwell", "endloop");
        Assert.Equal(0, e.TotalPlanned.TotalSeconds);   // nothing readable yet

        e.OnStepStarted(1, 0, "wait 30");
        Assert.Equal(60, e.Remaining!.Value.TotalSeconds);   // both iterations priced
    }

    [Fact]
    public void Remaining_MeasuredDurationStillBeatsAResolvedNominal()
    {
        var e = Make("loop 2", "swait pm 1mT $timeout", "endloop");

        e.OnStepStarted(1, 0, "swait pm 1mT 600");
        Assert.Equal(1200, e.Remaining!.Value.TotalSeconds);

        e.OnStepStarted(1, 45, "swait pm 1mT 600");   // it really took 45 s
        Assert.Equal(45, e.Remaining!.Value.TotalSeconds);
    }

    [Fact]
    public void Remaining_SkippedBranch_DropsOutOfTheEstimate()
    {
        var e = Make(
            "if pm < 1mT",
            "delay 60",
            "else",
            "delay 10",
            "endif",
            "delay 5");

        e.OnStepStarted(3, 0);   // the else branch ran: lines 0-2 never execute
        Assert.Equal(15, e.Remaining!.Value.TotalSeconds);
    }

    [Fact]
    public void Remaining_SteadyLoopIterations_NeverIncreases()
    {
        // Six stacks of identical work. Nothing about the run changes, so the
        // estimate has no honest reason to ever go up.
        var e = Make("loop 6", "delay 10", "deposit Cu 100 Home", "endloop");

        double t = 0;
        var seen = new List<double>();
        for (int iteration = 0; iteration < 6; iteration++)
        {
            e.OnStepStarted(1, t); seen.Add(e.Remaining!.Value.TotalSeconds);
            t += 10;
            e.OnStepStarted(2, t); seen.Add(e.Remaining!.Value.TotalSeconds);
            t += 100;
        }

        for (int i = 1; i < seen.Count; i++)
            Assert.True(seen[i] <= seen[i - 1],
                $"remaining rose from {seen[i - 1]:F1}s to {seen[i]:F1}s at sample {i}");
    }

    [Fact]
    public void Remaining_MixedStepDurationsInALoop_NeverJumpsUp()
    {
        // Cheap writes next to a long deposit: averaging seconds-per-step would make
        // every deposit push Remaining up by minutes; a duration plan must not care.
        // The tolerance covers honest learning only — a `valve` write declares 0 s
        // and measures ~0.2 s, so the iterations still to come get fractionally dearer.
        var e = Make(
            "loop 4",
            "valve M1 open",
            "mfc Ar1 20",
            "delay 5",
            "deposit Cu 200 Home",
            "valve M1 close",
            "endloop");

        double[] actual = [0.2, 0.2, 5, 200, 0.2];   // per line, every iteration
        double t = 0;
        var seen = new List<double>();
        for (int iteration = 0; iteration < 4; iteration++)
        {
            for (int line = 1; line <= 5; line++)
            {
                e.OnStepStarted(line, t);
                seen.Add(e.Remaining!.Value.TotalSeconds);
                t += actual[line - 1];
            }
        }

        for (int i = 1; i < seen.Count; i++)
            Assert.True(seen[i] - seen[i - 1] <= 1.0,
                $"remaining jumped from {seen[i - 1]:F1}s to {seen[i]:F1}s at sample {i}");
        // The run ends inside the last (0.2 s) valve write — nothing else left.
        Assert.True(seen[^1] < 1, $"expected the run to be all but over, got {seen[^1]:F1}s");
    }

    [Fact]
    public void Reset_DropsMeasurementsSoTheNextRunReplansFromNominals()
    {
        var e = Make("loop 2", "swait pm 1mT 600", "endloop");
        e.OnStepStarted(1, 0);
        e.OnStepStarted(1, 45);                              // one iteration measured at 45 s
        Assert.Equal(45, e.Remaining!.Value.TotalSeconds);   // the second is priced from it

        e.Reset();
        Assert.Null(e.Remaining);
        Assert.Equal(1200, e.TotalPlanned.TotalSeconds);
    }
}
