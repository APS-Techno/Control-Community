using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public class BaseStepperDrive : IBaseComponent
{
    private bool _connected;

    public BaseStepperDrive(IBaseDeviceParams deviceParams)
    {
        Params = deviceParams;
        Params.Type = DeviceType.Drive;
    }

    public IBaseDeviceParams Params { get; private set; }

    public bool Connected { get => _connected; set => _connected = value; }

    /// <summary>Current position in motor steps (updated by ReadPosition).</summary>
    public float CurrentPosition { get; protected set; }

    /// <summary>Commanded target position in motor steps.</summary>
    public float TargetPosition { get; set; }

    // Written by DriveStateReader polling thread, read by the recipe thread and
    // the GUI thread — use volatile backing fields for safe cross-thread visibility.
    private volatile bool _isMoving;
    private volatile bool _isHomed;

    public bool IsMoving
    {
        get => _isMoving;
        protected set => _isMoving = value;
    }

    /// <summary>Whether the drive has been homed since last power-on.</summary>
    public bool IsHomed
    {
        get => _isHomed;
        protected set => _isHomed = value;
    }

    /// <summary>
    /// Latest value of the drive's user status register, as last read by
    /// <see cref="ReadPosition"/>. Applied Motion drive status codes used
    /// across the stack: 22 = Moving, 51 = Arrived-to-position,
    /// 52 = Leaving-position, 99 = Complete, 0 = Idle.
    /// Exposed on the base so composite operations that speak to the drive
    /// through the abstract type can watch for sub-state transitions
    /// (e.g. Deposit waiting for "arrived" before starting its dwell timer).
    /// </summary>
    public int UserRegister { get; protected set; }

    /// <summary>
    /// True when the drive knows which flange it is parked at — either from a
    /// completed move/home in this session or recovered at connect time from
    /// the drive's status register (which survives Control restarts while the
    /// drive keeps power). False after a drive power cycle, when the position
    /// is unknown until the next Seek Home. Exposed on the base so the GUI's
    /// startup position check (Delphi CheckDrivePositions parity) doesn't need
    /// to cast to a concrete driver type.
    /// </summary>
    public virtual bool FlangePositionKnown => false;

    /// <summary>
    /// Position offset applied to all flange positions in motor steps.
    /// Concrete drivers override.
    /// </summary>
    public virtual int Offset { get; set; }

    /// <summary>Backlash-compensation offset applied to reverse-direction moves.</summary>
    public virtual int ReverseOffset { get; set; }

    /// <summary>
    /// Fires when <see cref="Offset"/> or <see cref="ReverseOffset"/> changes
    /// at runtime so the host can persist the new value (e.g. write back to
    /// HWConf.xml). Concrete drivers raise this from their property setters
    /// via <see cref="RaiseCalibrationChanged"/>.
    /// </summary>
    public event Action<BaseStepperDrive>? CalibrationChanged;

    protected void RaiseCalibrationChanged() => CalibrationChanged?.Invoke(this);

    /// <summary>Velocity setpoint (motor steps/sec).</summary>
    public float Velocity { get; set; }

    /// <summary>Acceleration setpoint (motor steps/sec^2).</summary>
    public float Acceleration { get; set; }

    /// <summary>Deceleration setpoint (motor steps/sec^2).</summary>
    public float Deceleration { get; set; }

    public virtual bool Connect() => false;

    /// <summary>Move to an absolute position in motor steps.</summary>
    public virtual void MoveTo(float position) { }

    /// <summary>Move to a predefined flange position (1-based index).</summary>
    public virtual void MoveToFlange(int flangeNum) { }

    /// <summary>Move to a predefined flange position using queue-buffered execution with status signals.</summary>
    public virtual void MoveToFlangeQueued(int flangeNum) => MoveToFlange(flangeNum);

    /// <summary>
    /// Manual jog to an absolute position in motor steps. Unlike
    /// <see cref="MoveToFlange"/> this leaves the drive at no named flange, so
    /// concrete drivers must invalidate whatever flange bookkeeping they keep.
    /// The default degrades to a plain <see cref="MoveTo"/>.
    /// </summary>
    public virtual void JogTo(float position) => MoveTo(position);

    public virtual void Home() { }

    public virtual void Scan(float start, float end, float speed) { }

    public virtual void ScanReciprocating(float start, float end, float speed, int count) { }

    /// <summary>
    /// Open shutter for deposition: move to pos1, dwell for duration, move to pos2.
    /// The drive executes the entire sequence autonomously via its command queue.
    /// </summary>
    public virtual void OpenShutterForDeposition(int pos1, int pos2, float durationSeconds) { }

    public virtual void Stop() { }

    /// <summary>
    /// Abort whatever the drive is executing on its own (a queued program such
    /// as a deposit dwell keeps running on the drive after the recipe task is
    /// cancelled) and reset the local motion state. Called on recipe abort /
    /// failure before the failure protocol, which mirrors the Delphi abort
    /// (Holder/Shutter Kill + Stop). Default: plain Stop().
    /// </summary>
    public virtual void AbortMotion() => Stop();

    /// <summary>Read current position and motion status from hardware. Updates CurrentPosition and IsMoving.</summary>
    public virtual void ReadPosition() { }

    public virtual void ProcessCommand(string command, object[] args)
    {
        switch (command)
        {
            case "JogTo" when args.Length >= 1:
                JogTo(Convert.ToSingle(args[0]));
                break;
            case "SetOffset" when args.Length >= 1:
                Offset = Convert.ToInt32(args[0]);
                break;
            case "SetReverseOffset" when args.Length >= 1:
                ReverseOffset = Convert.ToInt32(args[0]);
                break;
        }
    }
}

public class StepperDriveFactory : IDeviceFactory
{
    public IBaseComponent CreateDevice(IBaseDeviceParams deviceParams) => new BaseStepperDrive(deviceParams);
}
