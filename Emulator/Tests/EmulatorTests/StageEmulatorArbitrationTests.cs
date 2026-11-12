using EmulatorCore.Interfaces;
using APS.Emulator.Devices.S7;

namespace EmulatorTests;

public class StageEmulatorArbitrationTests
{
    private static StageEmulator MakeStage() =>
        new StageEmulator(id: "ARM", dbAddress: 152, triggerDbAddr: 7,
            triggerBitIndex: 1, description: "Test stage", host: "tm");

    [Fact]
    public void WriteSetpointFromWire_WhenLocked_DoesNotUpdateSetpoint()
    {
        var stage = MakeStage();
        bool locked = true;
        stage.SetRobotLockProvider(() => locked);

        stage.WriteSetpointFromWire(99.0f);

        Assert.Equal(0f, stage.Setpoint); // initial value untouched
    }

    [Fact]
    public void WriteSetpointFromWire_WhenUnlocked_UpdatesSetpoint()
    {
        var stage = MakeStage();
        bool locked = false;
        stage.SetRobotLockProvider(() => locked);

        stage.WriteSetpointFromWire(42.0f);

        Assert.Equal(42.0f, stage.Setpoint);
    }

    [Fact]
    public void WriteSetpointFromWire_LockReleasedMidRun_AllowsSubsequentWrite()
    {
        var stage = MakeStage();
        bool locked = true;
        stage.SetRobotLockProvider(() => locked);

        stage.WriteSetpointFromWire(99.0f);
        Assert.Equal(0f, stage.Setpoint); // still blocked

        locked = false;
        stage.WriteSetpointFromWire(42.0f);
        Assert.Equal(42.0f, stage.Setpoint); // now allowed
    }

    [Fact]
    public void SetSetpoint_ViaInterface_BypassesRobotLock()
    {
        // The IStageEmulator.SetSetpoint path (used by RobotEmulator) must NOT be gated.
        var stage = MakeStage();
        stage.SetRobotLockProvider(() => true); // always locked

        ((IStageEmulator)stage).SetSetpoint(77.0f);

        // Robot-side write succeeds even when lock is active
        Assert.Equal(77.0f, stage.Setpoint);
    }

    [Fact]
    public void NoLockProvider_WriteSetpointFromWire_AlwaysSucceeds()
    {
        var stage = MakeStage(); // no SetRobotLockProvider called

        stage.WriteSetpointFromWire(55.0f);

        Assert.Equal(55.0f, stage.Setpoint);
    }
}
