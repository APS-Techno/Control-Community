using APS.SystemControl.Core.Production;
using Xunit;

namespace CoreLogicTests;

public class TargetFormatMatchTests
{
    [Theory]
    // exact match (case-insensitive) -> compatible
    [InlineData("160mm", "160mm", true)]
    [InlineData("4\" round", "4\" ROUND", true)]
    // different format -> not compatible
    [InlineData("160mm", "4\" round", false)]
    // magnetron has no format -> everything compatible (nothing to filter on)
    [InlineData(null, "160mm", true)]
    [InlineData("", "4\" round", true)]
    [InlineData("   ", null, true)]
    // magnetron formatted but target blank -> not compatible
    [InlineData("160mm", null, false)]
    [InlineData("160mm", "", false)]
    [InlineData("160mm", "   ", false)]
    public void IsCompatible_matches_on_exact_format_or_blank_magnetron(
        string? magnetronFormat, string? targetFormat, bool expected)
        => Assert.Equal(expected, TargetFormatMatch.IsCompatible(magnetronFormat, targetFormat));
}
