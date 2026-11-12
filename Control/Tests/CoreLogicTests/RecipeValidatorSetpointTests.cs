using APS.RecipeModel.Validation;

namespace CoreLogicTests;

/// <summary>
/// Locks in the C2 phase-7 numeric-setpoint sanity check so operators can't
/// accidentally ship a recipe with NaN / negative / order-of-magnitude-
/// typo setpoints.
/// </summary>
public class RecipeValidatorSetpointTests
{
    private static RecipeValidator MakeValidator(
        IReadOnlyDictionary<string, (float Min, float Max)>? bounds = null)
    {
        string[] devices = ["Ar1", "N21", "PSA", "VS", "HS"];
        return new RecipeValidator(
            RecipeValidator.AllKnownCommands, devices,
            deviceBounds: bounds);
    }

    [Fact]
    public void Valid_mfc_setpoint_has_no_errors()
    {
        var errors = MakeValidator().Validate("mfc Ar1 5.0");
        Assert.Empty(errors);
    }

    [Fact]
    public void Mfc_above_default_ceiling_is_flagged()
    {
        // Ceiling is 1000 sccm — typo of 9999 caught.
        var errors = MakeValidator().Validate("mfc Ar1 9999");
        Assert.Contains(errors, e => e.Message.Contains("ceiling"));
    }

    [Fact]
    public void Stage_negative_position_is_allowed()
    {
        // Stage can legitimately park below home (e.g. HS -283 in LOAD_SUBSTRATE).
        // Lock in both: (a) the lexer keeps the sign on a literal that follows an
        // Identifier, and (b) the validator does not reject negative stage targets.
        var tokens = new Lexer().Tokenize("stage VS -10").Single();
        Assert.Equal(TokenType.Number, tokens[2].Type);
        Assert.Equal("-10", tokens[2].Value);

        var errors = MakeValidator().Validate("stage VS -10");
        Assert.DoesNotContain(errors, e => e.Message.Contains("non-negative"));
    }

    [Fact]
    public void Mfc_negative_setpoint_is_rejected()
    {
        // The lexer keeps the sign on `-5` after the `Ar1` identifier, so the
        // validator sees the negative and rejects it.
        var errors = MakeValidator().Validate("mfc Ar1 -5");
        Assert.Contains(errors, e => e.Message.Contains("non-negative"));
    }

    [Fact]
    public void DeviceBounds_override_default_ceiling()
    {
        // Ar1 has a tighter range than the default ceiling; 200 is over it.
        var bounds = new Dictionary<string, (float Min, float Max)>
        {
            ["Ar1"] = (0f, 100f),
        };
        var errors = MakeValidator(bounds).Validate("mfc Ar1 200");
        Assert.Contains(errors, e => e.Message.Contains("out of range"));
    }

    [Fact]
    public void DeviceBounds_in_range_passes()
    {
        var bounds = new Dictionary<string, (float Min, float Max)>
        {
            ["Ar1"] = (0f, 100f),
        };
        var errors = MakeValidator(bounds).Validate("mfc Ar1 50");
        Assert.Empty(errors);
    }

    [Fact]
    public void Bias_negative_is_rejected()
    {
        var errors = MakeValidator().Validate("bias -100");
        Assert.Contains(errors, e => e.Message.Contains("non-negative"));
    }

    [Fact]
    public void Bias_above_ceiling_is_flagged()
    {
        // bias ceiling = 2000 V
        var errors = MakeValidator().Validate("bias 5000");
        Assert.Contains(errors, e => e.Message.Contains("ceiling"));
    }

    [Fact]
    public void Power_command_with_numeric_setpoint_respects_ceiling()
    {
        var errors = MakeValidator().Validate("power PSA 25000");
        Assert.Contains(errors, e => e.Message.Contains("ceiling"));
    }

    [Fact]
    public void Non_numeric_power_argument_does_not_trip_phase7()
    {
        // "power PSA on" is a toggle, not a setpoint — phase 7 must ignore it.
        var errors = MakeValidator().Validate("power PSA on");
        Assert.DoesNotContain(errors, e => e.Message.Contains("setpoint"));
    }

    [Fact]
    public void Isolate_zero_arg_keyword_validates_cleanly()
    {
        // "isolate" takes no arguments — validator must accept it without error.
        var errors = MakeValidator().Validate("isolate");
        Assert.Empty(errors);
    }

    [Fact]
    public void Isolate_inside_recipe_block_validates_cleanly()
    {
        const string recipe = """
            begin
            mfc Ar1 5
            isolate
            end
            """;
        var errors = MakeValidator().Validate(recipe);
        Assert.Empty(errors);
    }
}
