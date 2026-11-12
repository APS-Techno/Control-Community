using APS.Controls.Generic;
using Xunit;

namespace CoreLogicTests;

public class ChamberSchematicFormatTests
{
    [Fact]
    public void FormatDepositionTime_Null_ReturnsPlaceholder()
    {
        Assert.Equal("--", ChamberSchematicControl.FormatDepositionTime(null));
    }

    [Theory]
    [InlineData(0.0,     "0.00 sec")]
    [InlineData(30.0,   "30.00 sec")]
    [InlineData(12.5,   "12.50 sec")]
    [InlineData(99.99,  "99.99 sec")]
    public void FormatDepositionTime_Under100Seconds_FormatsAsSecondsWithTwoDecimals(
        double seconds, string expected)
    {
        Assert.Equal(expected, ChamberSchematicControl.FormatDepositionTime(seconds));
    }

    [Theory]
    [InlineData(100.0,  "1 min 40 sec")]
    [InlineData(120.0,  "2 min 0 sec")]
    [InlineData(150.9,  "2 min 31 sec")]
    [InlineData(3600.0, "60 min 0 sec")]
    public void FormatDepositionTime_AtOrAbove100Seconds_FormatsAsMinutesAndSeconds(
        double seconds, string expected)
    {
        Assert.Equal(expected, ChamberSchematicControl.FormatDepositionTime(seconds));
    }
}
