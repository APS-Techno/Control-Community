using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class RecipeLogPolicyTests
{
    [Theory]
    // isUser, logUser, logEmbedded => expected
    [InlineData(true,  true,  false, true)]   // user run, user logging on
    [InlineData(true,  false, true,  false)]  // user run, user logging off
    [InlineData(false, true,  false, false)]  // embedded run, embedded off (default)
    [InlineData(false, true,  true,  true)]   // embedded run, embedded on
    public void ShouldLog_selects_flag_by_recipe_class(
        bool isUser, bool logUser, bool logEmbedded, bool expected)
    {
        Assert.Equal(expected, RecipeLogPolicy.ShouldLog(isUser, logUser, logEmbedded));
    }
}
