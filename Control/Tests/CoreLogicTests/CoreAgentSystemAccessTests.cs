using APS.Controls.Core;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Deposition guard of CoreAgentSystemAccess.SendDeviceCommand: rejects manual Drive/Stage commands and
/// Valve commands on a source shutter while a deposition is active; Power (and everything else)
/// is never gated, so an operator can always kill power.
/// </summary>
public class CoreAgentSystemAccessTests
{
    [Theory]
    [InlineData(DeviceType.Drive, false, true)]
    [InlineData(DeviceType.Drive, true, true)]
    [InlineData(DeviceType.Stage, false, true)]
    [InlineData(DeviceType.Stage, true, true)]
    [InlineData(DeviceType.Valve, true, true)]
    [InlineData(DeviceType.Valve, false, false)]
    [InlineData(DeviceType.Power, false, false)]
    [InlineData(DeviceType.Power, true, false)]
    [InlineData(DeviceType.MFC, false, false)]
    [InlineData(DeviceType.MFC, true, false)]
    public void Deposition_guard_gates_drive_stage_and_shutter_valves_only(
        DeviceType type, bool isShutter, bool gated) =>
        Assert.Equal(gated, CoreAgentSystemAccess.IsDepositionGated(type, isShutter));
}
