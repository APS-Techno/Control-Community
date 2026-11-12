namespace APS.SystemControl.Core.HWClasses.Base;

/// <summary>
/// Frozen wire constants for the transfer robot protocol.
/// Mirrored on the Emulator side and (future) PLC ladder side.
/// Changes here are breaking changes to the device protocol —
/// bump the lib version and update HWConf samples.
/// </summary>
public static class RobotWireContract
{
    // Command word layout (4 bytes from base+cmdOffset)
    public const int CmdLength = 4;
    public const int CmdOff_Cmd   = 0;
    public const int CmdOff_Src   = 1;
    public const int CmdOff_Dst   = 2;
    public const int CmdOff_CmdId = 3;

    // Status word layout (8 bytes from base+statusOffset)
    public const int StatusLength = 8;
    public const int StatusOff_State       = 0;
    public const int StatusOff_Phase       = 1;
    public const int StatusOff_CurrentFace = 2;
    public const int StatusOff_Flags       = 3;
    public const int StatusOff_ErrorCode   = 4;
    public const int StatusOff_LastCmdId   = 5;
    public const int StatusOff_AngleNorm   = 6;
    public const int StatusOff_ExtendNorm  = 7;

    // Flags byte bits — both occupy the single byte at StatusOff_Flags; bitwise-OR to combine.
    public const byte Flag_HasWafer = 0x01;
    public const byte Flag_Homed    = 0x02;

    public const byte ParkFace = 0;
}

public enum RobotCommand : byte
{
    None     = 0,
    Home     = 1,
    Transfer = 2,
    Abort    = 3,
}

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
    // Note: destination side rotates BEFORE opening its slit (asymmetric with the
    // source side, which opens its slit first while still at park, then rotates).
    // After phase 6 the source slit is closed and the arm is still at the source
    // face, so phase 7 rotates the arm clear of the source chamber before
    // exposing the destination chamber in phase 8.
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
