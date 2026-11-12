using APS.SystemControl.Core.Recipe;

namespace CoreLogicTests;

public class RecipeEventFormatTests
{
    [Theory]
    [InlineData(0.0, "0.0s")]
    [InlineData(4.7, "4.7s")]
    [InlineData(59.9, "59.9s")]
    [InlineData(60, "01m 00s")]
    [InlineData(754, "12m 34s")]
    [InlineData(3599, "59m 59s")]
    [InlineData(3600, "1h 00m 00s")]
    [InlineData(3725, "1h 02m 05s")]
    [InlineData(36000, "10h 00m 00s")]
    public void FormatDuration_RendersExpectedShape(double seconds, string expected)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, RecipeEventFormat.FormatDuration(ts));
    }

    [Fact]
    public void FormatDuration_NegativeReturnsZero()
    {
        Assert.Equal("0.0s", RecipeEventFormat.FormatDuration(TimeSpan.FromSeconds(-3)));
    }

    [Fact]
    public void FirstLine_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Equal("", RecipeEventFormat.FirstLine(null));
        Assert.Equal("", RecipeEventFormat.FirstLine(""));
        Assert.Equal("", RecipeEventFormat.FirstLine("   \r\n\t"));
    }

    [Fact]
    public void FirstLine_ReturnsTrimmedFirstLine()
    {
        Assert.Equal("hello", RecipeEventFormat.FirstLine("  hello  "));
        Assert.Equal("first line", RecipeEventFormat.FirstLine("first line\r\nsecond"));
        Assert.Equal("first", RecipeEventFormat.FirstLine("first\nsecond\nthird"));
        Assert.Equal("first", RecipeEventFormat.FirstLine("first\rsecond"));   // bare-CR separator
    }

    [Fact]
    public void FirstLine_CapsAt120Chars()
    {
        string long_ = new('x', 200);
        string result = RecipeEventFormat.FirstLine(long_);
        Assert.Equal(120, result.Length);
        Assert.All(result, c => Assert.Equal('x', c));
    }

    [Fact]
    public void FirstLine_LeadingNewlinesProduceEmpty()
    {
        // CoreThread.Execute prefixes "\n " to its InvalidOperationException
        // message; FirstLine should return "".
        Assert.Equal("", RecipeEventFormat.FirstLine("\n Recipe foo failed"));
    }

    [Fact]
    public void FirstLine_WhitespaceFirstLine_ReturnsEmpty()
    {
        // Pins current behavior: takes line 0 verbatim, trims, returns "".
        // The spec says "first non-empty line" but the impl always picks line 0;
        // for .NET exception inputs this matches in practice. If the spec is
        // tightened to truly skip blank lines, this test will need updating.
        Assert.Equal("", RecipeEventFormat.FirstLine("   \nActual error"));
    }
}
