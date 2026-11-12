using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

public class SanitizeSetpointTests
{
    // Thin subclass so we can hit the protected static helper.
    private sealed class TestPsu : BasePowerSupply
    {
        public TestPsu() : base(ParamsConverter.Params("t", 0, "test")) { }
        public static float Invoke(float value, float max, string name)
            => SanitizeSetpoint(value, max, name);
    }

    [Fact]
    public void Clamps_negative_to_zero()
    {
        Assert.Equal(0f, TestPsu.Invoke(-5f, 100f, "Voltage"));
    }

    [Fact]
    public void Clamps_above_max_to_max()
    {
        Assert.Equal(100f, TestPsu.Invoke(250f, 100f, "Voltage"));
    }

    [Fact]
    public void Passes_through_in_range_value()
    {
        Assert.Equal(42.5f, TestPsu.Invoke(42.5f, 100f, "Voltage"));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void Rejects_non_finite(float bad)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => TestPsu.Invoke(bad, 100f, "Voltage"));
        Assert.Equal("Voltage", ex.ParamName);
    }

    [Fact]
    public void Accepts_zero_and_max_boundary()
    {
        Assert.Equal(0f,   TestPsu.Invoke(0f,   100f, "X"));
        Assert.Equal(100f, TestPsu.Invoke(100f, 100f, "X"));
    }
}
