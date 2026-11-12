using APS.SystemControl.BaseHWClasses;

namespace APS.Hardware.S7;

/// <summary>
/// Momentary command-flag pulse (set bit, hold, clear bit).
///
/// The clearing write is safety-critical: a comm fault that skips it leaves the
/// flag latched high, and a later opposite command would assert both paired
/// flags. The clear is therefore attempted in a finally and retried once after a
/// transient fault before the failure propagates.
/// </summary>
internal static class PlcPulse
{
    internal const int DefaultPulseMs = 250;

    internal static void Pulse(IPLCController plc, int mode, int address, int bit, int pulseMs = DefaultPulseMs)
    {
        plc.WriteBool(mode, address, bit, true);
        try
        {
            Task.Delay(pulseMs).Wait();
        }
        finally
        {
            try
            {
                plc.WriteBool(mode, address, bit, false);
            }
            catch (PlcCommException)
            {
                // One retry: a transient fault on the clear must not leave the
                // command flag latched. A second failure propagates loudly —
                // the operator needs to know the flag may be stuck.
                Task.Delay(pulseMs).Wait();
                plc.WriteBool(mode, address, bit, false);
            }
        }
    }
}
