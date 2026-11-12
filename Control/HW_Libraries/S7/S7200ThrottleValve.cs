using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.Hardware.S7;

/// <summary>
/// PLC-hosted throttle valve. PLC firmware runs the PID; the host
/// writes a target pressure (Torr) to <c>address+0</c> and reads back
/// position (%) from <c>address+4</c>.
///
/// Setpoint sentinels:
///   0      → fully open (no regulation, full pumping)
///   9999+  → drive fully closed
///   else   → engage PID at that pressure
/// </summary>
public class S7200ThrottleValve : BaseController
{
    private const float OpenSentinel = 0f;
    private const float CloseSentinel = 9999f;
    private const float VerifyToleranceTorr = 0.0001f;
    private const int VerifyMaxAttempts = 5;

    private readonly IPLCController _plc;
    private readonly int _address;
    private float? _lastSetpoint;
    private float _lastPosition;

    public S7200ThrottleValve(IPLCController plc, string id, int address, string description)
        : base(new BaseDeviceParams(id, address, description))
    {
        _plc = plc;
        _address = address;
    }

    public override bool Connect()
    {
        _connected = _plc.Connected;
        return _connected;
    }

    public override void Disconnect() { _connected = false; }

    public override object[] Measure()
    {
        if (_plc.Connected)
            _lastPosition = _plc.ReadFloat((int)Mode.DB, _address + 4);
        return [_lastPosition, _lastSetpoint ?? 0f];
    }

    public override void Command(object[] args)
    {
        if (args.Length == 0) return;
        switch (args[0] as string)
        {
            case "Open":
                WriteSetpoint(OpenSentinel);
                break;
            case "Close":
                WriteSetpoint(CloseSentinel);
                break;
            case "Stop":
                if (_lastSetpoint is float held)
                    WriteSetpoint(held);
                break;
            case "SetPressure":
            {
                float pressure = Convert.ToSingle(args[1]);
                if (float.IsNaN(pressure) || float.IsInfinity(pressure) || pressure < 0f)
                    throw new ArgumentOutOfRangeException(
                        nameof(args), pressure,
                        "S7200ThrottleValve target pressure must be a finite non-negative number");
                WriteSetpoint(pressure);
                break;
            }
            case "SetPoint":
            case "SetPointValues":
            case "Z1":
                System.Diagnostics.Trace.TraceWarning(
                    $"S7200ThrottleValve[{Params.ID}] verb '{args[0]}' is serial-controller-only and ignored.");
                break;
        }
    }

    private void WriteSetpoint(float value)
    {
        bool ok = PlcVerifiedWrites.WriteFloatVerified(
            _plc, (int)Mode.DB, _address,
            value: value, tolerance: VerifyToleranceTorr, maxAttempts: VerifyMaxAttempts);
        if (!ok)
        {
            System.Diagnostics.Trace.TraceError(
                $"S7200ThrottleValve[{Params.ID}] write {value:F4} Torr failed verification after {VerifyMaxAttempts} attempts.");
            return;
        }
        _lastSetpoint = value;
    }
}
