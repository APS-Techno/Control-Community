using APS.SystemControl.Core.Production;
using Xunit;

namespace CoreLogicTests;

public class TargetGeometryTests
{
    [Theory]
    [InlineData("4\" round", 50.8)]
    [InlineData("4in-round", 50.8)]
    [InlineData("2\" round", 25.4)]
    [InlineData(null, 50.8)]            // default
    [InlineData("weird", 50.8)]        // default
    public void RadiusFromFormat_parses_inch_diameter_to_mm_radius(string? format, double expected)
        => Assert.Equal(expected, TargetGeometry.RadiusFromFormat(format), 2);
}
