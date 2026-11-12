namespace APS.ProcessPanel.Contracts;

/// <summary>Byte-for-byte mirror of APS.SystemControl.Core.SystemState. Keep in sync;
/// parity enforced by CoreLogicTests.ClusterStateParityTests.</summary>
public enum SystemStateCode : byte
{
    Startup     = 0,
    Idle        = 1,
    Maintenance = 2,
    Stopped     = 3,
    Processing  = 4,
    Unknown     = 5,
}

/// <summary>Byte-for-byte mirror of APS.SystemControl.Core.HWClasses.Base.RobotState.
/// Keep in sync; parity enforced by CoreLogicTests.ClusterStateParityTests.</summary>
public enum RobotStateCode : byte
{
    Startup      = 0,
    NotHomed     = 1,
    Homing       = 2,
    Idle         = 3,
    Transferring = 4,
    Error        = 5,
}
