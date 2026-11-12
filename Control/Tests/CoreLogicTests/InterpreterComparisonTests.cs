using APS.SystemControl.Core.Recipe;

namespace CoreLogicTests;

/// <summary>
/// Comparisons use a relative tolerance, not <c>float.Epsilon</c>, so <c>if $pressure = 1.0</c>
/// matches sensor readings near the target.
/// </summary>
public class InterpreterComparisonTests
{
    private static bool Eval(float lhs, string op, float rhs)
        => Interpreter.EvaluateComparison(lhs, op, rhs, sourceLine: 0);

    [Theory]
    [InlineData("<",  1.0f, 2.0f, true)]
    [InlineData("<",  2.0f, 1.0f, false)]
    [InlineData(">",  2.0f, 1.0f, true)]
    [InlineData(">",  1.0f, 2.0f, false)]
    [InlineData("<=", 1.0f, 1.0f, true)]
    [InlineData(">=", 1.0f, 1.0f, true)]
    public void Ordered_comparisons(string op, float lhs, float rhs, bool expected)
    {
        Assert.Equal(expected, Eval(lhs, op, rhs));
    }

    [Fact]
    public void Equal_matches_exact()
    {
        Assert.True(Eval(3.14f, "=",  3.14f));
        Assert.True(Eval(3.14f, "==", 3.14f));
    }

    [Fact]
    public void Equal_matches_within_relative_tolerance()
    {
        // Scale 1000 => tolerance ~0.01.
        Assert.True(Eval(1000.001f, "=",  1000.0f));
        Assert.True(Eval(1000.001f, "==", 1000.0f));
    }

    [Fact]
    public void Equal_does_not_match_outside_tolerance()
    {
        Assert.False(Eval(1001.0f, "=",  1000.0f));
    }

    [Fact]
    public void NotEqual_mirrors_equal()
    {
        Assert.False(Eval(1000.001f, "!=", 1000.0f));
        Assert.True (Eval(1001.0f,   "!=", 1000.0f));
    }

    [Fact]
    public void Unknown_operator_throws()
    {
        Assert.Throws<InvalidOperationException>(() => Eval(1.0f, "~", 1.0f));
    }
}
