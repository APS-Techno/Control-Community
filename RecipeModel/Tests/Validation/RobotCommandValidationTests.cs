using APS.RecipeModel.Validation;
using Xunit;

namespace APS.RecipeModel.Tests.Validation;

public class RobotCommandValidationTests
{
    private static RecipeValidator Make(params string[] faces) =>
        new(RecipeValidator.AllKnownCommands, knownDevices: [],
            knownSourceNames: null, knownEmbeddedRecipes: null,
            deviceBounds: null, throttleValveSensor: null, knownFaces: faces);

    [Fact]
    public void KnownFaces_NoErrors()
    {
        var errors = Make("LL", "C1").Validate("transfer LL C1\nhome\nabort\n");
        Assert.Empty(errors);
    }

    [Fact]
    public void UnknownFace_ReportsError()
    {
        var errors = Make("LL", "C1").Validate("transfer LL Xyz\n");
        Assert.Contains(errors, e => e.Message.Contains("Xyz"));
    }

    [Fact]
    public void ParkAndHome_AlwaysValid()
    {
        Assert.Empty(Make("LL").Validate("transfer park Home\n"));
    }

    [Fact]
    public void VarRefAndNumericFaces_Skipped()
    {
        // $from is undeclared so Phase 4 reports it, but the face-validation
        // phase must not add a face error for the $var or the numeric index.
        var errors = Make("LL").Validate("transfer $from 3\n");
        Assert.DoesNotContain(errors, e => e.Message.Contains("robot face"));
        Assert.Single(errors);
        Assert.Contains(errors, e => e.Message.Contains("from"));
    }

    [Fact]
    public void BareDollar_DoesNotSwallow_NextFace()
    {
        // A lone '$' (no following Identifier) must advance only one token, so the
        // Number '3' is read as the second face arg and 'Xyz', a third token, is
        // never face-checked.
        var errors = Make("LL", "C1").Validate("transfer $ 3 Xyz\n");
        Assert.DoesNotContain(errors, e => e.Message.Contains("robot face"));
    }

    [Fact]
    public void NoFaceCatalog_SkipsFaceCheck()
    {
        var v = new RecipeValidator(RecipeValidator.AllKnownCommands, knownDevices: []);
        Assert.DoesNotContain(v.Validate("transfer LL C1\n"),
            e => e.Message.Contains("face"));
    }

    [Fact]
    public void TransferMissingArg_ReportsArgCountError()
    {
        Assert.Contains(Make("LL", "C1").Validate("transfer LL\n"),
            e => e.Message.Contains("'transfer' requires at least 2 argument"));
    }
}
