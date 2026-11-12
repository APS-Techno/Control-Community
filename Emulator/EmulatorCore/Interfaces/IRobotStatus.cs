namespace EmulatorCore.Interfaces;

public enum RobotState : byte
{
    Startup = 0, NotHomed = 1, Homing = 2, Idle = 3, Transferring = 4, Error = 5
}

public enum RobotPhase : byte
{
    None = 0,
    OpenSrcSlit = 1, RotateToSrc = 2, ExtendToSrc = 3, LiftAtSrc = 4,
    RetractFromSrc = 5, CloseSrcSlit = 6,
    RotateToDst = 7, OpenDstSlit = 8, ExtendToDst = 9, LowerAtDst = 10,
    RetractFromDst = 11, CloseDstSlit = 12,
}

public enum RobotErrorCode : byte
{
    Ok = 0, Aborted = 1, NotHomed = 2, BadCmd = 3,
    RotateTimeout = 4, ExtendTimeout = 5, LiftTimeout = 6, SlitTimeout = 7,
    WaferLost = 8,
}

public interface IRobotStatus : IDeviceEmulator
{
    bool HasWafer { get; }
    RobotErrorCode LastErrorCode { get; }
    byte TransferSrc { get; }
    byte TransferDst { get; }
    IReadOnlyCollection<string> SlitValveIds { get; }
    byte LastCmdId { get; }
    RobotState GetState();
    RobotPhase GetPhase();
    byte GetCurrentFace();
    string FaceName(byte idx);
}
