using APS.ProcessPanel.Contracts;
using APS.SystemControl;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Readback → process-panel reading mapping. The panel shows voltage and
/// current for DC supplies on one chart and watts for RF, so every supply
/// publishes all three under <c>&lt;id&gt;_V / _I / _W</c> and the panel's
/// spec list picks what it draws. The BIAS-1's four-channel readback and the
/// GAUGE_A throttle controller's ["Data", pressure, position, ..] shape are
/// recognised by shape, not by device name.
/// </summary>
public class PsuTelemetryMapperTests
{
    private static ReadingState Ok(string _, double __) => ReadingState.Ok;

    [Fact]
    public void SingleChannel_publishes_V_I_W_with_classified_state()
    {
        object[] args = [412f, 730f, 300f, true];
        var s = PsuTelemetryMapper.MapPowerSupply("PSU-A", args, Ok);

        Assert.Equal(3, s.Count);
        Assert.Contains(s, x => x.Id == "PSU-A_V" && x.Value == 412.0 && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "PSU-A_I" && x.Value == 730.0 && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "PSU-A_W" && x.Value == 300.0 && x.State == ReadingState.Ok);
    }

    [Fact]
    public void SingleChannel_output_off_marks_every_sample_Off()
    {
        object[] args = [0f, 0f, 0f, false, 1, 50f];
        var s = PsuTelemetryMapper.MapPowerSupply("PSU-B", args, Ok);

        Assert.Equal(3, s.Count);
        Assert.All(s, x => Assert.Equal(ReadingState.Off, x.State));
    }

    [Fact]
    public void SingleChannel_classifier_is_applied_to_the_watts_only()
    {
        object[] args = [500f, 100f, 900f, true];
        var s = PsuTelemetryMapper.MapPowerSupply("RF301", args,
            (id, v) => id == "RF301_W" && v > 800 ? ReadingState.Warning : ReadingState.Ok);

        Assert.Equal(ReadingState.Warning, s.Single(x => x.Id == "RF301_W").State);
        Assert.Equal(ReadingState.Ok,      s.Single(x => x.Id == "RF301_V").State);
    }

    [Fact]
    public void MultiChannel_publishes_channels_1_and_2_live_regardless_of_output_flag()
    {
        // [V1, I1, V2, I2, V3, I3, V4, I4, Out1..Out4, IsResponding]
        // The BIAS-1 is also the bias meter: a channel with output off still
        // reads the line, so its samples never carry Off.
        object[] args = [-80f, 0.125f, 12f, 0.5f, 0f, 0f, 0f, 0f, true, false, false, false, true];
        var s = PsuTelemetryMapper.MapPowerSupply("BIAS-1", args, Ok);

        Assert.Equal(4, s.Count);
        Assert.Contains(s, x => x.Id == "BIAS-1_V1" && x.Value == -80.0 && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "BIAS-1_I1" && x.Value == 0.125f && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "BIAS-1_V2" && x.Value == 12.0  && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "BIAS-1_I2" && x.Value == 0.5   && x.State == ReadingState.Ok);
    }

    [Fact]
    public void Unrecognised_shape_yields_nothing()
    {
        Assert.Empty(PsuTelemetryMapper.MapPowerSupply("X", [1f, 2f], Ok));
        Assert.Empty(PsuTelemetryMapper.MapPowerSupply("X", ["Data", 1f, 2f], Ok));
        Assert.Empty(PsuTelemetryMapper.MapPowerSupply("X", [], Ok));
    }

    [Fact]
    public void GAUGE_A_publishes_pressure_and_position_under_the_device_id()
    {
        object[] args = ["Data", 2.5f, 38.4f, 3f, 1, false];
        var s = PsuTelemetryMapper.MapThrottleController("GAUGE_A#1", args);

        Assert.Equal(2, s.Count);
        Assert.Contains(s, x => x.Id == "GAUGE_A#1_P"   && x.Value == 2.5   && x.State == ReadingState.Ok);
        Assert.Contains(s, x => x.Id == "GAUGE_A#1_Pos" && x.Value == 38.4f && x.State == ReadingState.Ok);
    }

    [Fact]
    public void GAUGE_A_ignores_the_PLC_throttle_shape()
    {
        Assert.Empty(PsuTelemetryMapper.MapThrottleController("C1_TV", [45f, 50f]));
    }

    [Fact]
    public void PsuTelemetryMapper_singleChannelReadback_yieldsWatts()
    {
        object[] args = [412f, 730f, 300f, true];

        Assert.True(PsuTelemetryMapper.TryGetSingleChannelWatts(args, out float watts));
        Assert.Equal(300f, watts);
    }

    [Fact]
    public void PsuTelemetryMapper_multiChannelReadback_yieldsNoWatts()
    {
        object[] args = [-80f, 0.125f, 12f, 0.5f, 0f, 0f, 0f, 0f, true, false, false, false, true];

        Assert.False(PsuTelemetryMapper.TryGetSingleChannelWatts(args, out _));
    }

    [Fact]
    public void PsuTelemetryMapper_shortReadback_yieldsNoWatts()
    {
        Assert.False(PsuTelemetryMapper.TryGetSingleChannelWatts([1f, 2f], out _));
    }
}
