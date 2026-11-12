using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Recipe;

namespace RecipeTests;

public class RecipeCallTests
{
    [Fact]
    public void IsInterpreterKeyword_RecognizesCall()
    {
        Assert.True(Interpreter.IsInterpreterKeyword("call"));
        Assert.True(Interpreter.IsInterpreterKeyword("CALL"));
        Assert.True(Interpreter.IsInterpreterKeyword("Call"));
    }

    [Fact]
    public void ResolveEmbeddedRecipe_NoResolver_ReturnsNull()
    {
        var interpreter = new Interpreter();
        Assert.Null(interpreter.ResolveEmbeddedRecipe("ANYTHING"));
    }

    [Fact]
    public void ResolveEmbeddedRecipe_DelegatesToCallback()
    {
        var interpreter = new Interpreter();
        interpreter.EmbeddedRecipeResolver = name =>
            name == "KNOWN" ? ["delay 1", "delay 2"] : null;

        var lines = interpreter.ResolveEmbeddedRecipe("KNOWN");
        Assert.NotNull(lines);
        Assert.Equal(2, lines.Length);
        Assert.Equal("delay 1", lines[0]);

        Assert.Null(interpreter.ResolveEmbeddedRecipe("UNKNOWN"));
    }

    [Fact]
    public void EnterCall_ExitCall_BalancesDepth()
    {
        var interpreter = new Interpreter();
        // Should be allowed 8 deep
        for (int i = 0; i < 8; i++)
            interpreter.EnterCall(sourceLine: 1, targetName: "X");
        for (int i = 0; i < 8; i++)
            interpreter.ExitCall();

        interpreter.EnterCall(sourceLine: 1, targetName: "X");
        interpreter.ExitCall();
    }

    [Fact]
    public void EnterCall_ExceedsMaxDepth_Throws()
    {
        var interpreter = new Interpreter();
        for (int i = 0; i < Interpreter.MaxCallDepth; i++)
            interpreter.EnterCall(sourceLine: 1, targetName: "X");

        var ex = Assert.Throws<InvalidOperationException>(
            () => interpreter.EnterCall(sourceLine: 42, targetName: "BOOM"));
        Assert.Contains("'call BOOM'", ex.Message);
        Assert.Contains("call depth", ex.Message);
        Assert.Contains("8", ex.Message);
    }

    [Fact]
    public void Reset_ClearsCallDepth()
    {
        var interpreter = new Interpreter();
        for (int i = 0; i < Interpreter.MaxCallDepth; i++)
            interpreter.EnterCall(sourceLine: 1, targetName: "X");

        interpreter.Reset();

        interpreter.EnterCall(sourceLine: 1, targetName: "X");
        interpreter.ExitCall();
    }

    [Fact]
    public void Validator_CallWithName_NoErrors()
    {
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: [],
            knownSourceNames: null,
            knownEmbeddedRecipes: ["LOAD_SUBSTRATE"]);
        var errors = validator.Validate("call LOAD_SUBSTRATE");
        Assert.DoesNotContain(errors, e => e.Message.Contains("'call'"));
        Assert.DoesNotContain(errors, e => e.Message.Contains("embedded recipe"));
    }

    [Fact]
    public void Validator_CallWithoutName_ReportsError()
    {
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: [],
            knownSourceNames: null,
            knownEmbeddedRecipes: ["LOAD_SUBSTRATE"]);
        var errors = validator.Validate("call");
        Assert.Contains(errors, e => e.Message.Contains("'call' requires at least 1 argument"));
    }

    [Fact]
    public void Validator_CallUnknownRecipe_ReportsError()
    {
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: [],
            knownSourceNames: null,
            knownEmbeddedRecipes: ["LOAD_SUBSTRATE"]);
        var errors = validator.Validate("call NOT_A_REAL_RECIPE");
        Assert.Contains(errors, e => e.Message.Contains("unknown embedded recipe 'NOT_A_REAL_RECIPE'"));
    }

    [Fact]
    public void Validator_CallNoRegistry_SkipsExistenceCheck()
    {
        // No knownEmbeddedRecipes supplied — validator can't check existence,
        // so the runtime catches it. We just want NO unknown-recipe error here.
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: []);
        var errors = validator.Validate("call ANYTHING");
        Assert.DoesNotContain(errors, e => e.Message.Contains("unknown embedded recipe"));
    }

    [Fact]
    public void Validator_CallIsRecognizedAsKnownCommand()
    {
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: []);
        var errors = validator.Validate("call FOO");
        Assert.DoesNotContain(errors, e => e.Message.Contains("Unknown command 'call'"));
    }
}
