using APS.RecipeModel.Validation;

namespace CoreLogicTests;

/// <summary>
/// Only <c>_i</c> may be referenced without a <c>var</c> declaration —
/// <c>Interpreter.ExpandLoop</c> sets it each iteration. Nothing supplies
/// <c>$pressure</c> / <c>$temperature</c> / <c>$time</c>: <c>Interpreter.SystemVariableProvider</c>
/// is never assigned and <c>Core.LoadWorker.LoadVars</c> is an empty stub. Such a reference
/// survives substitution as the literal text <c>$pressure</c> and throws in the downstream
/// numeric parse, so the author must be told at edit time.
/// </summary>
public class SystemVariableValidationTests
{
    private static RecipeValidator V() => new(
        RecipeValidator.AllKnownCommands,
        knownDevices: ["pm", "pl", "Ar1"]);

    [Fact]
    public void Loop_counter_needs_no_declaration()
    {
        var errors = V().Validate("loop 3\n  mfc Ar1 $_i\nendloop");
        Assert.DoesNotContain(errors, e => e.Message.Contains("Undefined variable"));
    }

    [Theory]
    [InlineData("pressure")]
    [InlineData("temperature")]
    [InlineData("time")]
    public void Unimplemented_live_variables_are_reported(string name)
    {
        var errors = V().Validate($"delay ${name}");
        Assert.Contains(errors, e => e.Message.Contains($"Undefined variable '${name}'"));
    }

    [Fact]
    public void Declared_variable_of_the_same_name_is_still_fine()
    {
        // The runtime does not reserve "pressure", so an author may declare it.
        var errors = V().Validate("var pressure 50mT\nswait pm $pressure 300");
        Assert.DoesNotContain(errors, e => e.Message.Contains("Undefined variable"));
    }
}
