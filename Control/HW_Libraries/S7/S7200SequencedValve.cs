using APS.SystemControl.BaseHWClasses;

namespace APS.Hardware.S7;

/// <summary>
/// A valve commanded through a separate DB1 flag but read back from its M-bit,
/// because firmware recomputes the M-bit every scan and would revert a direct
/// write. All per-valve differences are configuration:
///   address / channel = the M-bit read back for status
///   cmdAddress / cmdBit = the DB1 command flag written
///   cmdInvert = 1 when the flag is active-low
/// </summary>
public class S7200SequencedValve(
    IPLCController PLC, string id, int address, int channel, string description,
    int cmdAddress, int cmdBit, int cmdInvert)
    : S7200Valve(PLC, id, address, channel, description)
{
    private const int VerifyMaxAttempts = 5;
    private readonly bool invert = cmdInvert != 0;

    protected override void WriteHW(bool newstate)
    {
        // Verify against the flag written, not the firmware-owned M-bit, which races the PLC scan.
        bool cmd = invert ? !newstate : newstate;
        bool ok = PlcVerifiedWrites.WriteBoolVerified(
            PLC, (int)Mode.DB, cmdAddress, cmdBit,
            value: cmd, maxAttempts: VerifyMaxAttempts);
        if (!ok)
        {
            System.Diagnostics.Trace.TraceError(
                $"S7200SequencedValve[{Params.ID}] command write {(newstate ? "OPEN" : "CLOSE")} " +
                $"(DB1.{cmdAddress}.{cmdBit}{(invert ? " inverted" : "")}) failed verification after {VerifyMaxAttempts} attempts.");
            RaiseWriteFailed(newstate);
        }
    }
}
