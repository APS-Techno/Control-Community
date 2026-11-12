using System.Globalization;
using APS.Controls.Dialogs;
using Xunit;

namespace APS.Controls.Tests.Dialogs;

public class FloatInputDialogTests
{
    [Theory]
    [InlineData("1.5", 1.5f)]
    [InlineData("-10.5", -10.5f)]
    [InlineData("0.005", 0.005f)]
    [InlineData(" 2.5 ", 2.5f)]
    [InlineData("42", 42f)]
    public void TryParseFloat_accepts_point_decimal_on_any_locale(string text, float expected)
    {
        Assert.True(FloatInputDialog.TryParseFloat(text, out float result));
        Assert.Equal(expected, result, precision: 5);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1.2.3")]
    public void TryParseFloat_rejects_bad_input(string text)
    {
        Assert.False(FloatInputDialog.TryParseFloat(text, out _));
    }

    [Fact]
    public void TryParseFloat_accepts_both_separators_on_comma_locale()
    {
        var prev = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");

            Assert.True(FloatInputDialog.TryParseFloat("1.5", out float pt));
            Assert.Equal(1.5f, pt, precision: 5);

            Assert.True(FloatInputDialog.TryParseFloat("1,5", out float cm));
            Assert.Equal(1.5f, cm, precision: 5);
        }
        finally
        {
            CultureInfo.CurrentCulture = prev;
        }
    }
}
