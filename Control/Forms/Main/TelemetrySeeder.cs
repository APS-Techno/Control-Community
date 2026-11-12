using APS.ProcessPanel.Contracts;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl;

/// <summary>
/// Primes a freshly created <see cref="ProcessTelemetryCollector"/> from the
/// devices' in-memory caches.
///
/// Why: Core's 200 ms poll change-gates every non-sensor Value message
/// (<c>Core.ShouldForwardPolledValue</c>). A supply or throttle controller
/// sends its first readback while the Process panel is still being built —
/// before the collector exists to receive it — and then, as long as the
/// readback does not change, never sends another. Without seeding, the throttle
/// controller and bias rows sat at "off" until an operator toggled something on the
/// supply's panel. Reads only cached values (LastMeasurement,
/// GetReadback, LastFlow, LastValue) — no hardware I/O, so it is safe on the
/// UI thread.
/// </summary>
internal static class TelemetrySeeder
{
    /// <summary>Push every connected device's cached reading; returns the sample count.</summary>
    public static int Seed(IEnumerable<IBaseComponent> devices, ProcessTelemetryCollector collector,
        Func<string, double, ReadingState> classify)
    {
        int n = 0;
        foreach (var d in devices)
        {
            if (!d.Connected) continue;
            string id = d.Params.ID;
            switch (d)
            {
                case BasePowerSupply psu:
                    foreach (var s in PsuTelemetryMapper.MapPowerSupply(id, psu.GetReadback(), classify))
                    { collector.Push(s.Id, s.Value, s.State); n++; }
                    break;
                case BaseController ctrl:
                    foreach (var s in PsuTelemetryMapper.MapThrottleController(id, ctrl.LastMeasurement))
                    { collector.Push(s.Id, s.Value, s.State); n++; }
                    break;
                case BaseMFC mfc:
                    collector.Push(id, mfc.LastFlow, classify(id, mfc.LastFlow)); n++;
                    break;
                case BaseSensor sensor:
                    collector.Push(id, sensor.LastValue, classify(id, sensor.LastValue)); n++;
                    break;
            }
        }
        return n;
    }
}
