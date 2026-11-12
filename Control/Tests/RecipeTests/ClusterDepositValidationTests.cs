using APS.RecipeModel.Validation;
using Xunit;

namespace RecipeTests;

public class ClusterDepositValidationTests
{
    [Fact]
    public void Deposit_material_time_is_accepted()
    {
        var validator = new RecipeValidator(
            RecipeValidator.AllKnownCommands,
            knownDevices: ["C1_Sh", "C1_RF"],
            knownSourceNames: ["Cu", "Ti", "W", "Mo"]);

        var errors = validator.Validate("deposit Cu 60");

        Assert.DoesNotContain(errors, e => e.Message.Contains("Unknown command"));
        Assert.DoesNotContain(errors, e => e.Message.Contains("deposit"));
    }
}
