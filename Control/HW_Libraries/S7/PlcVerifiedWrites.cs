using APS.SystemControl.BaseHWClasses;

namespace APS.Hardware.S7;

/// <summary>
/// Write-with-readback-verify helpers with a bounded retry loop. Returns true
/// if a readback within tolerance was observed within maxAttempts; false if every
/// attempt was dropped or read back wrong.
/// </summary>
public static class PlcVerifiedWrites
{
    public static bool WriteFloatVerified(
        IPLCController plc, int mode, int address,
        float value, float tolerance, int maxAttempts = 5)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                plc.WriteFloat(mode, address, value);
                float readback = plc.ReadFloat(mode, address);
                if (Math.Abs(readback - value) <= tolerance)
                    return true;
            }
            catch (PlcCommException)
            {
                // Transient comm failure: retry within the attempt budget.
            }
        }
        return false;
    }

    public static bool WriteBoolVerified(
        IPLCController plc, int mode, int address, int byteIndex,
        bool value, int maxAttempts = 5)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            try
            {
                plc.WriteBool(mode, address, byteIndex, value);
                bool readback = plc.ReadBool(mode, address, byteIndex);
                if (readback == value)
                    return true;
            }
            catch (PlcCommException)
            {
                // Transient comm failure: retry within the attempt budget.
            }
        }
        return false;
    }
}
