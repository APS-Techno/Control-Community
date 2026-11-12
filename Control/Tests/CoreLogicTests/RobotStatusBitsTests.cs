using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// BaseRobot implements IStatusBitNamed (so a
/// device="ROBOT" status="error" alarm rule binds at load instead of being
/// dropped) and exposes a status-bit array whose index 0 reflects a fault.
/// </summary>
public class RobotStatusBitsTests
{
    private sealed class TestRobot : BaseRobot
    {
        public TestRobot() : base(new BaseDeviceParams("ROBOT", 0, "test")) { }
        public void SetState(RobotState s) => State = s;
        public void SetError(RobotErrorCode e) => ErrorCode = e;
        public override void Poll() { }
        public override void Transfer(byte src, byte dst) { }
        public override void Home() { }
        public override void Abort() { }
    }

    [Fact]
    public void Robot_ImplementsIStatusBitNamed_WithErrorBit()
    {
        var robot = new TestRobot();
        Assert.IsAssignableFrom<IStatusBitNamed>(robot);
        Assert.Equal(new[] { "error" }, robot.StatusBitNames);
    }

    [Fact]
    public void StatusBits_FalseWhenHealthy()
    {
        var robot = new TestRobot();   // RobotState.Startup + RobotErrorCode.Ok
        Assert.False(robot.StatusBits()[0]);
    }

    [Fact]
    public void StatusBits_TrueOnErrorState()
    {
        var robot = new TestRobot();
        robot.SetState(RobotState.Error);
        Assert.True(robot.StatusBits()[0]);
    }

    [Fact]
    public void StatusBits_TrueOnNonOkErrorCode()
    {
        var robot = new TestRobot();
        robot.SetState(RobotState.Idle);
        robot.SetError(RobotErrorCode.SlitTimeout);
        Assert.True(robot.StatusBits()[0]);
    }
}
