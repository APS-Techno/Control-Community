using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class TimeParserTests
{
    [Theory]
    [InlineData("30", 30f)]
    [InlineData("0", 0f)]
    [InlineData("1.5", 1.5f)]
    public void BareNumber_IsSeconds(string input, float expected)
    {
        Assert.Equal(expected, TimeParser.ParseSeconds(input), 6);
    }

    [Theory]
    [InlineData("30s", 30f)]
    [InlineData("30m", 1800f)]
    [InlineData("1h", 3600f)]
    [InlineData("1.5h", 5400f)]
    [InlineData("90m", 5400f)]
    [InlineData("0.5m", 30f)]
    public void Suffix_ConvertsToSeconds(string input, float expected)
    {
        Assert.Equal(expected, TimeParser.ParseSeconds(input), 4);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("30x")]      // unknown unit
    [InlineData("s")]        // suffix only, no number
    [InlineData("30ms")]     // ms not supported
    public void Invalid_Throws(string input)
    {
        Assert.Throws<FormatException>(() => TimeParser.ParseSeconds(input));
    }

    [Fact]
    public void TryParse_Succeeds_OnValid()
    {
        Assert.True(TimeParser.TryParseSeconds("2m", out float secs));
        Assert.Equal(120f, secs, 4);
    }

    [Theory]
    [InlineData("fast")]
    [InlineData("30ms")]
    [InlineData("")]
    public void TryParse_Fails_OnInvalid(string input)
    {
        Assert.False(TimeParser.TryParseSeconds(input, out _));
    }
}
