using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

/// <summary>
/// H7 — the recipe "Run" button refuses to fire a recipe silently while
/// overrides are active. The gate decision lives in RecipeRunGate so it
/// can be tested without a live form.
/// </summary>
public class RecipeRunGateTests
{
    [Fact]
    public void No_overrides_does_not_warn()
    {
        Assert.False(RecipeRunGate.ShouldWarn(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50)]
    public void Any_overrides_warns(int count)
    {
        Assert.True(RecipeRunGate.ShouldWarn(count));
    }

    [Fact]
    public void Warning_message_includes_override_count()
    {
        string msg = RecipeRunGate.BuildWarningMessage(3);
        Assert.Contains("3", msg);
        Assert.Contains("override", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Warning_message_calls_out_unsafety_and_prompts_for_confirmation()
    {
        // Locks in the phrasing: operators must see "unsafe" and be asked
        // to authorise — no silent start-anyway.
        string msg = RecipeRunGate.BuildWarningMessage(1);
        Assert.Contains("unsafe", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("anyway", msg, StringComparison.OrdinalIgnoreCase);
    }
}
