using APS.Controls.Vacuum;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;

namespace APS.SystemControl;

/// <summary>
/// Routes the transfer-module widget's service actions (Home / Abort /
/// Transfer) through the HW channel as worker operations, like every other
/// manual control. Calling BaseRobot methods directly in the click handler
/// would do synchronous S7-TCP round-trips on the UI thread (UI frozen for the
/// socket timeout on a slow/half-open link), contend with CoreThread's socket
/// lock, and drop a failed write silently. Posting to the HW channel serializes
/// PLC access on CoreThread, runs the interlock checks, and surfaces failures
/// in the operator event log. (code audit 2026-08-29, C8)
/// </summary>
internal static class TransferWidgetWiring
{
    internal static ParamMsg HomeMsg(string robotId) =>
        new(MsgType.Command, DeviceType.Robot, "home", [DeviceType.Robot, robotId]);

    internal static ParamMsg AbortMsg(string robotId) =>
        new(MsgType.Command, DeviceType.Robot, "abort", [DeviceType.Robot, robotId]);

    internal static ParamMsg TransferMsg(string robotId, string srcFace, string dstFace) =>
        new(MsgType.Command, DeviceType.Robot, "transfer", [DeviceType.Robot, robotId, srcFace, dstFace]);

    /// <summary>Byte face index → configured face name (the worker ops resolve
    /// faces by name). Unknown index degrades to its number so the worker
    /// reports "unknown face" instead of the UI throwing.</summary>
    internal static string FaceName(IReadOnlyDictionary<string, byte> faces, byte face)
    {
        foreach (var kv in faces)
            if (kv.Value == face)
                return kv.Key;
        return face.ToString();
    }

    internal static void Wire(TransferModuleControl widget, BaseRobot robot, Action<ThreadMsg> writeHW)
    {
        string id = robot.Params.ID;
        widget.HomeRequested     += (_, _) => writeHW(HomeMsg(id));
        widget.AbortRequested    += (_, _) => writeHW(AbortMsg(id));
        widget.TransferRequested += (_, e) => writeHW(TransferMsg(id,
            FaceName(robot.Faces, e.src), FaceName(robot.Faces, e.dst)));
    }
}
