namespace APS.Controls.Vacuum;

// Mirror of APS.SystemControl.Core.HWClasses.Base.RobotWireContract enums.
// VacuumControls cannot reference Control (circular dependency); the host
// (FrmMain) bridges the two by casting:
//   widget.State = (RobotState)(byte)robot.State;
// Keep this file byte-for-byte identical to the corresponding enums in
// Control/Core/HWClasses/Base/RobotWireContract.cs.

public enum RobotState : byte
{
    Startup      = 0,
    NotHomed     = 1,
    Homing       = 2,
    Idle         = 3,
    Transferring = 4,
    Error        = 5,
}

public enum RobotPhase : byte
{
    None             = 0,
    OpenSrcSlit      = 1,
    RotateToSrc      = 2,
    ExtendToSrc      = 3,
    LiftAtSrc        = 4,
    RetractFromSrc   = 5,
    CloseSrcSlit     = 6,
    RotateToDst      = 7,
    OpenDstSlit      = 8,
    ExtendToDst      = 9,
    LowerAtDst       = 10,
    RetractFromDst   = 11,
    CloseDstSlit     = 12,
}

public enum RobotErrorCode : byte
{
    Ok              = 0,
    Aborted         = 1,
    NotHomed        = 2,
    BadCmd          = 3,
    RotateTimeout   = 4,
    ExtendTimeout   = 5,
    LiftTimeout     = 6,
    SlitTimeout     = 7,
    WaferLost       = 8,
}
