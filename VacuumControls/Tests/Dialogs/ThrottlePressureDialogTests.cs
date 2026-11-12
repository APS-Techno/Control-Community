using APS.Controls.Dialogs;
using Xunit;

namespace APS.Controls.Tests.Dialogs;

public class ThrottlePressureDialogTests
{
    [Theory]
    [InlineData("5mT", 0.005f)]
    [InlineData("0.005", 0.005f)]
    [InlineData("1uT", 0.000001f)]
    [InlineData("2T", 2f)]
    [InlineData("100nT", 0.0000001f)]
    public void TryParsePressure_accepts_suffixed_inputs(string text, float expectedTorr)
    {
        Assert.True(ThrottlePressureDialog.TryParsePressure(text, out float torr));
        Assert.Equal(expectedTorr, torr, precision: 7);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("5xT")]
    public void TryParsePressure_rejects_bad_input(string text)
    {
        Assert.False(ThrottlePressureDialog.TryParsePressure(text, out _));
    }
}
