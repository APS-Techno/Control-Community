using APS.SystemControl.Core;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

public abstract class BaseRobot : IBaseComponent, IStatusBitNamed
{
    public IBaseDeviceParams Params { get; }
    public bool Connected { get; set; }

    /// <summary>
    /// Chamber whose alarm rules own this robot's device-status alarm (a transfer
    /// robot lives on the transfer module). Set from HWConf's
    /// <c>chamber="..."</c>; defaults to <see cref="CoreConstants.DefaultChamberId"/>.
    /// </summary>
    public string Chamber { get; set; } = CoreConstants.DefaultChamberId;

    /// <summary>
    /// Device-status alarm bit names (index order). AlarmConfigLoader drops any
    /// <c>status="error"</c> robot rule on a device without IStatusBitNamed.
    /// </summary>
    public IReadOnlyList<string> StatusBitNames { get; } = new[] { "error" };

    /// <summary>
    /// Current device-status bits in <see cref="StatusBitNames"/> order, built
    /// from the last <see cref="Poll"/>. Index 0 = faulted.
    /// </summary>
    public bool[] StatusBits()
        => [State == RobotState.Error || ErrorCode != RobotErrorCode.Ok];

    // Latest cached status — refreshed by Poll()
    public RobotState State { get; protected set; } = RobotState.Startup;
    public RobotPhase Phase { get; protected set; } = RobotPhase.None;
    public byte CurrentFace { get; protected set; }
    public bool HasWafer { get; protected set; }
    public bool Homed { get; protected set; }
    public RobotErrorCode ErrorCode { get; protected set; } = RobotErrorCode.Ok;
    public byte LastCmdId { get; protected set; }

    /// <summary>
    /// CmdId of the most recently issued command, set by the concrete driver
    /// when it writes the command word. The wire contract's completion
    /// handshake is <c>LastCmdId == LastIssuedCmdId</c> (the ladder echoes the
    /// id it consumed) — bare State==Idle testing can accept a poll that lands
    /// before the ladder latches the command.
    /// </summary>
    public byte LastIssuedCmdId { get; protected set; }
    public byte AngleNorm { get; protected set; }
    public byte ExtendNorm { get; protected set; }

    /// <summary>
    /// Face-name → face-index mapping, populated by the HWConf loader from
    /// <c>&lt;face&gt;</c> child elements. Empty until the loader runs.
    /// </summary>
    public IReadOnlyDictionary<string, byte> Faces { get; set; }
        = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Face-name → slit-valve-ID mapping, populated by the HWConf loader
    /// from the <c>slit="…"</c> attribute on each <c>&lt;face&gt;</c> element.
    /// Used by FrmMain.UpdateControls to look up the live valve state for each
    /// slit glyph rendered on the TransferModuleControl hex edges.
    /// Empty until the loader runs; faces without a <c>slit</c> attribute are omitted.
    /// </summary>
    public IReadOnlyDictionary<string, string> FaceSlitMap { get; set; }
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Face-name → face-angle (degrees, clockwise from 12 o'clock) mapping,
    /// populated by the HWConf loader from the <c>angle="…"</c> attribute on
    /// each <c>&lt;face&gt;</c>. Used by the cluster process panel to place each
    /// chamber tile radially. Empty until the loader runs; faces without an
    /// <c>angle</c> attribute are omitted.
    /// </summary>
    public IReadOnlyDictionary<string, int> FaceAngleMap { get; set; }
        = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    protected BaseRobot(IBaseDeviceParams parms)
    {
        Params = parms;
        Params.Type = DeviceType.Robot;
    }

    public virtual bool Connect() => Connected;

    /// <summary>Reads the device's status word and updates cached properties.</summary>
    public abstract void Poll();

    /// <summary>Issues a transfer command with a fresh cmdId.</summary>
    public abstract void Transfer(byte srcFace, byte dstFace);

    /// <summary>Issues a home command with a fresh cmdId.</summary>
    public abstract void Home();

    /// <summary>Issues an abort command with a fresh cmdId.</summary>
    public abstract void Abort();
}
