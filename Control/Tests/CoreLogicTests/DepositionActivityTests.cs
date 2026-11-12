using APS.SystemControl.Core;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class DepositionActivityTests
{
    [Fact]
    public void No_recipe_and_all_supplies_off_is_inactive()
        => Assert.False(DepositionActivity.IsActive(recipeRunning: false, new[] { 0.0, 0.0 }));

    [Fact]
    public void Any_supply_drawing_power_is_active()
        => Assert.True(DepositionActivity.IsActive(recipeRunning: false, new[] { 0.0, 12.5 }));

    [Fact]
    public void Running_recipe_is_active_even_with_no_power()
        => Assert.True(DepositionActivity.IsActive(recipeRunning: true, new[] { 0.0 }));

    [Fact]
    public void Gas_only_no_recipe_no_power_is_inactive()
        => Assert.False(DepositionActivity.IsActive(recipeRunning: false, System.Array.Empty<double>()));
}
