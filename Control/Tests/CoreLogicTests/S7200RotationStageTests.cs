using APS.SystemControl.BaseHWClasses;
using APS.Hardware.S7;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

public class S7200RotationStageTests
{
    private static S7200RotationStage NewStage(FakePlcController fake)
        => new(fake, id: "rotation",
               address: 1974, feedbackAddr: 1978,
               enableByte: 30, enableBit: 0,
               description: "Substrate Rotation");

    [Fact]
    public void WriteHW_SpeedAboveOne_WritesSpeedAndSetsEnableBit()
    {
        var fake = new FakePlcController();
        var stage = NewStage(fake);

        stage.Position = 25f;

        Assert.Equal(25f, fake.ReadFloat((int)Mode.DB, 1974));
        Assert.True(fake.ReadBool((int)Mode.MB, 30, 0));
    }

    [Fact]
    public void WriteHW_SpeedZero_ClearsEnableBitAndSkipsSpeedWrite()
    {
        var fake = new FakePlcController();
        // Pre-arm the enable bit so we can observe it being cleared.
        fake.WriteBool((int)Mode.MB, 30, 0, true);
        int floatWritesBefore = fake.FloatWrites;
        var stage = NewStage(fake);

        stage.Position = 0f;

        Assert.False(fake.ReadBool((int)Mode.MB, 30, 0));
        Assert.Equal(floatWritesBefore, fake.FloatWrites);
    }

    [Fact]
    public void WriteHW_SpeedAtThreshold_ClearsEnableBit()
    {
        // Enable iff speed > 1. A value of exactly 1 disables.
        var fake = new FakePlcController();
        fake.WriteBool((int)Mode.MB, 30, 0, true);
        var stage = NewStage(fake);

        stage.Position = 1f;

        Assert.False(fake.ReadBool((int)Mode.MB, 30, 0));
    }

    [Fact]
    public void ReadHW_ReadsFromFeedbackAddress_NotWriteAddress()
    {
        var fake = new FakePlcController();
        // Distinct values at the two addresses prove ReadHW reads feedbackAddr.
        fake.WriteFloat((int)Mode.DB, 1974, 99f);   // setpoint slot
        fake.WriteFloat((int)Mode.DB, 1978, 42f);   // feedback slot
        var stage = NewStage(fake);

        Assert.Equal(42f, stage.Position);
    }
}
