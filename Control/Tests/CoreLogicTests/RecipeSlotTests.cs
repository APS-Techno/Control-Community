using System.Threading.Channels;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Guards the single-flight recipe slot (audit #3) and the contract the recipe
/// finally blocks rely on (audit #1 review B1): once a recipe claims the slot,
/// a second is rejected, and EndRecipeSlot ALWAYS frees it so a transient fault
/// in the recipe-end path can never permanently lock out recipe execution.
/// </summary>
public class RecipeSlotTests
{
    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public void Slot_starts_free()
    {
        var core = NewCore();
        Assert.False(core.IsRecipeRunning);
        Assert.True(core.TryBeginRecipeSlot());
    }

    [Fact]
    public void Second_claim_is_rejected_while_one_is_active()
    {
        var core = NewCore();
        Assert.True(core.TryBeginRecipeSlot());
        Assert.True(core.IsRecipeRunning);

        // A second Execute must be rejected, NOT allowed to cancel/collide with
        // the running recipe.
        Assert.False(core.TryBeginRecipeSlot());
        Assert.True(core.IsRecipeRunning);   // the first is still the owner
    }

    [Fact]
    public void EndRecipeSlot_frees_the_slot_for_a_new_recipe()
    {
        var core = NewCore();
        Assert.True(core.TryBeginRecipeSlot());
        core.EndRecipeSlot();
        Assert.False(core.IsRecipeRunning);

        // After release a new recipe can claim it — the property the recipe
        // finally guarantees even when OnRecipeEnd/StopDataLogging throw.
        Assert.True(core.TryBeginRecipeSlot());
        core.EndRecipeSlot();
    }

    [Fact]
    public void EndRecipeSlot_is_idempotent()
    {
        var core = NewCore();
        core.EndRecipeSlot();   // releasing when not held is a no-op
        Assert.False(core.IsRecipeRunning);
        Assert.True(core.TryBeginRecipeSlot());
    }
}
