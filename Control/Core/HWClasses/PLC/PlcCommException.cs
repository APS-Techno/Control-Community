namespace APS.SystemControl.BaseHWClasses;

/// <summary>
/// Thrown when an S7 read/write returns a non-zero Sharp7 result code.
///
/// A failed scalar read would otherwise return the zero-initialised buffer
/// (<c>false</c> / <c>0</c> / <c>0.0f</c>), which is indistinguishable from a
/// genuine PLC reading and could feed a fabricated "valve closed / interlock
/// clear / pressure zero" into interlock, alarm and state-machine logic. The
/// exception lets the polling layer keep the last-known value instead.
///
/// Lives in <c>APS.SystemControl.BaseHWClasses</c> (alongside
/// <see cref="IPLCController"/>) so the dynamically-loaded HW drivers can throw
/// it AND the upstream Core can catch it by type — the driver assemblies
/// reference this project, not the other way round.
/// </summary>
public sealed class PlcCommException : Exception
{
    public int ResultCode { get; }

    public PlcCommException(int resultCode, string operation)
        : base($"PLC {operation} failed with Sharp7 result code 0x{resultCode:X}.")
    {
        ResultCode = resultCode;
    }
}
