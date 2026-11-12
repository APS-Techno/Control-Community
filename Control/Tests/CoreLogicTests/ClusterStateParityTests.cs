using APS.ProcessPanel.Contracts;
using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

public class ClusterStateParityTests
{
    [Theory]
    [InlineData(SystemState.Startup,     SystemStateCode.Startup)]
    [InlineData(SystemState.Idle,        SystemStateCode.Idle)]
    [InlineData(SystemState.Maintenance, SystemStateCode.Maintenance)]
    [InlineData(SystemState.Stopped,     SystemStateCode.Stopped)]
    [InlineData(SystemState.Processing,  SystemStateCode.Processing)]
    [InlineData(SystemState.Unknown,     SystemStateCode.Unknown)]
    public void SystemState_mirror_matches_byte(SystemState core, SystemStateCode mirror)
        => Assert.Equal((byte)core, (byte)mirror);

    [Theory]
    [InlineData(RobotState.Startup,      RobotStateCode.Startup)]
    [InlineData(RobotState.NotHomed,     RobotStateCode.NotHomed)]
    [InlineData(RobotState.Homing,       RobotStateCode.Homing)]
    [InlineData(RobotState.Idle,         RobotStateCode.Idle)]
    [InlineData(RobotState.Transferring, RobotStateCode.Transferring)]
    [InlineData(RobotState.Error,        RobotStateCode.Error)]
    public void RobotState_mirror_matches_byte(RobotState core, RobotStateCode mirror)
        => Assert.Equal((byte)core, (byte)mirror);
}
