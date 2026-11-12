using APS.SystemControl.Core.Classes;

namespace CoreLogicTests;

public class PressureParserTests
{
    [Theory]
    [InlineData("0.05", 0.05f)]
    [InlineData("1e-6", 1e-6f)]
    [InlineData("760", 760f)]
    [InlineData("0", 0f)]
    public void BareNumber_IsTorr(string input, float expected)
    {
        Assert.Equal(expected, PressureParser.ParseTorr(input), 6);
    }

    [Theory]
    [InlineData("50T",     50f)]
    [InlineData("0.05T",   0.05f)]
    [InlineData("50mT",    0.05f)]
    [InlineData("1uT",     1e-6f)]
    [InlineData("1µT",     1e-6f)]
    [InlineData("100nT",   100e-9f)]
    [InlineData("2kT",     2000f)]
    public void ShortSuffix_ConvertsToTorr(string input, float expected)
    {
        Assert.Equal(expected, PressureParser.ParseTorr(input), 9);
    }

    [Theory]
    [InlineData("50Torr",   50f)]
    [InlineData("50mTorr",  0.05f)]
    [InlineData("1uTorr",   1e-6f)]
    [InlineData("100nTorr", 100e-9f)]
    [InlineData("2kTorr",   2000f)]
    public void LongSuffix_ConvertsToTorr(string input, float expected)
    {
        Assert.Equal(expected, PressureParser.ParseTorr(input), 9);
    }

    [Theory]
    [InlineData("1e-6T",    1e-6f)]
    [InlineData("1.5e3mT",  1.5f)]      // 1500 mT = 1.5 T
    [InlineData("5e-3Torr", 5e-3f)]
    public void ScientificNotation_WithSuffix_Works(string input, float expected)
    {
        Assert.Equal(expected, PressureParser.ParseTorr(input), 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("50xT")]
    [InlineData("Torr")]       // suffix only, no number
    [InlineData("1e-6Pa")]     // unsupported unit
    public void Invalid_Throws(string input)
    {
        Assert.Throws<FormatException>(() => PressureParser.ParseTorr(input));
    }

    [Fact]
    public void CaseSensitive_Megaprefix_NotMilli()
    {
        // 'M' is mega, 'm' is milli — they must NOT alias.
        // We don't support 'MT' (megatorr) — should fail rather than be parsed as milli.
        Assert.Throws<FormatException>(() => PressureParser.ParseTorr("50MT"));
    }
}
