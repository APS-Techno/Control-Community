using APS.SystemControl.Core.Evidence;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class RunActualsAccumulatorTests
{
    [Fact]
    public void Means_AveragePerChannel_BasePressureIsMin()
    {
        var a = new RunActualsAccumulator();
        a.ConfigurePressure("pm", "mTorr");
        a.Sample("PSU-A", 200); a.Sample("PSU-A", 300);  // mean 250
        a.Sample("pm", 5); a.Sample("pm", 1); a.Sample("pm", 3); // min 1

        Assert.Equal(250, a.Means()["PSU-A"]);
        var bp = a.BasePressureOrNull();
        Assert.NotNull(bp);
        Assert.Equal("pm", bp!.Channel);
        Assert.Equal(1, bp.Min);
        Assert.Equal("mTorr", bp.Unit);
    }

    [Fact]
    public void NoSamples_NoBasePressure_AndResetClears()
    {
        var a = new RunActualsAccumulator();
        a.ConfigurePressure("pm", "mTorr");
        Assert.Null(a.BasePressureOrNull());
        a.Sample("pm", 2);
        a.Reset();
        Assert.Empty(a.Means());
        Assert.Null(a.BasePressureOrNull());
    }
}
