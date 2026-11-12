using APS.SystemControl.Controls.Theme;
using Xunit;

namespace CoreLogicTests;

public class LifeGaugeTests
{
    [Theory]
    [InlineData(0.0,  "green")]
    [InlineData(0.79, "green")]
    [InlineData(0.80, "amber")]
    [InlineData(0.99, "amber")]
    [InlineData(1.0,  "red")]
    [InlineData(1.5,  "red")]
    public void Bands_at_default_warn(double fraction, string band)
    {
        var expected = band switch
        {
            "green" => LifeGauge.Green,
            "amber" => LifeGauge.Amber,
            _       => LifeGauge.Red,
        };
        Assert.Equal(expected, LifeGauge.ColorFor(fraction, warnFraction: 0.8));
    }
}
