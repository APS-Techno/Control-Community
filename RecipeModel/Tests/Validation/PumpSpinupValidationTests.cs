using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class PumpSpinupValidationTests
{
    private static RecipeValidator Make() =>
        new(RecipeValidator.AllKnownCommands, knownDevices: ["SP1"],
            knownSourceNames: null, knownEmbeddedRecipes: null,
            deviceBounds: null, throttleValveSensor: null, knownFaces: []);

    [Fact]
    public void PumpOn_WithSpinupSeconds_IsValid()
    {
        var errors = Make().Validate("pump SP1 on 30\n");
        Assert.Empty(errors);
    }

    [Fact]
    public void PumpOn_PlainForm_StillValid()
    {
        var errors = Make().Validate("pump SP1 on\n");
        Assert.Empty(errors);
    }
}
