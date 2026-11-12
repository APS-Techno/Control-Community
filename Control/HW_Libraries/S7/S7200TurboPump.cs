using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

/// <summary>
/// Turbo / molecular pump on an S7-200 PLC. Status is a single byte read from DB
/// at the address parameter. On/off is written one of two ways, selected by the
/// constructor:
///   - a single MB on/off marker (writeAddr / writeIndex);
///   - a momentary pulse on a paired start/stop flag in DB1.
/// </summary>
public class S7200TurboPump : PlcPump, IStatusBitNamed
{
    private readonly IPLCController plc;
    private readonly bool pulseMode;
    private readonly int onAddr, onBit, offAddr, offBit;
    private const int PulseMs = 250;

    /// <summary>Single MB on/off marker at (writeAddr, writeIndex).</summary>
    public S7200TurboPump(IPLCController PLC, string id, int address, int writeAddr, int writeIndex, string description)
        : base(PLC, ConvertToParams(id, address, description))
    {
        plc = PLC;
        pulseMode = false;
        onAddr = offAddr = writeAddr;
        onBit = offBit = writeIndex;
    }

    /// <summary>Pulse a paired start/stop flag (DB1).</summary>
    public S7200TurboPump(IPLCController PLC, string id, int address, int startAddr, int startBit, int stopAddr, int stopBit, string description)
        : base(PLC, ConvertToParams(id, address, description))
    {
        plc = PLC;
        pulseMode = true;
        onAddr = startAddr; onBit = startBit;
        offAddr = stopAddr; offBit = stopBit;
    }

    private static BaseDeviceParams ConvertToParams(string id, int address, string description)
    {
        return new BaseDeviceParams(id, address, description);
    }

    public IReadOnlyList<string> StatusBitNames { get; } = new[]
    {
        "accel",
        "normal",
        "rotating",
        "alarm",
    };

    protected override bool ReadHW()
    {
        // Pulse mode has no echoed command bit; report the real state from the status byte.
        if (pulseMode) return IsRunning;
        return plc.ReadBool((int)Mode.MB, onAddr, onBit);
    }

    protected override void WriteHW(bool newstate)
    {
        if (!pulseMode)
        {
            plc.WriteBool((int)Mode.MB, onAddr, onBit, newstate);
            return;
        }

        int addr = newstate ? onAddr : offAddr;
        int bit = newstate ? onBit : offBit;
        PlcPulse.Pulse(plc, (int)Mode.DB, addr, bit, PulseMs);
    }

    /// <summary>The raw status register as last read from the PLC.</summary>
    public byte LastStatusByte { get; private set; }

    private int lastLoggedStatus = -1;

    protected override bool[] ReadStatus()
    {
        // One byte at the configured status address. A word read would also pull the
        // next byte, whose bits are unrelated flags.
        byte raw = plc.ReadByte((int)Mode.DB, Params.Address);
        LastStatusByte = raw;
        LogStatusChange(raw);

        bool bit1 = (raw & (1 << 1)) != 0;
        bool bit2 = (raw & (1 << 2)) != 0;
        bool bit3 = (raw & (1 << 3)) != 0;
        bool bit4 = (raw & (1 << 4)) != 0;
        bool bit5 = (raw & (1 << 5)) != 0;

        return [
            bit1 && bit2 && bit4,
            bit1 && bit3,
            bit1 && bit4,
            bit5
        ];
    }

    /// <summary>Publishes the raw status register to the event log (detail level) whenever it changes.</summary>
    private void LogStatusChange(byte raw)
    {
        if (raw == lastLoggedStatus) return;
        lastLoggedStatus = raw;

        var bits = new List<string>();
        for (int i = 0; i < 8; i++)
            if ((raw & (1 << i)) != 0) bits.Add(i.ToString());

        APS.SystemControl.Core.Logging.EventLogBus.Publish(
            Params.ID,
            $"status register DB{Params.Address} = 0x{raw:X2} " +
            $"({Convert.ToString(raw, 2).PadLeft(8, '0')}), " +
            $"bits set: {(bits.Count == 0 ? "none" : string.Join(",", bits))}",
            APS.SystemControl.Core.Logging.LogEventLevel.Detail);
    }

    /// <summary>
    /// Running means normal (at-speed) or rotating. status[0] is the acceleration
    /// transient, which clears at speed, so it is not used. Overriding the decoder
    /// keeps the cached running flag consistent with the live read.
    /// </summary>
    protected override bool IsRunningFrom(bool[] status)
        => status.Length >= 3 && (status[1] || status[2]);
}
