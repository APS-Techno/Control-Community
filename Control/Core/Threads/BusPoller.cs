using System.Diagnostics;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.Core.Threads;

/// <summary>
/// Polls one hardware bus's devices into their in-memory caches, in a loop, at
/// the bus's natural rate. Each device read is timeout-bounded so one hung
/// device cannot wedge the bus. Stamps <see cref="LastTickUtc"/> every completed
/// cycle so the watchdog can detect a stalled bus.
/// </summary>
public sealed class BusPoller
{
    private readonly IReadOnlyList<IBaseComponent> _devices;
    private readonly Action? _prePass;
    private readonly int _minCyclePeriodMs;
    private long _lastTickUtc;

    public string Name { get; }

    /// <param name="prePass">Optional per-cycle action run before the per-device
    /// Poll loop (the PLC bus uses it for the bulk valve read).</param>
    /// <param name="minCyclePeriodMs">Minimum wall-clock period of one full cycle.
    /// The loop waits out the remainder after the reads so a fast single-device
    /// bus can't hammer the device. 0 keeps a 1 ms spin-floor (device
    /// reads pace the loop) — correct for multi-device PLC buses. A single serial
    /// gauge/PSU needs an explicit period (e.g. 200 ms) or one short read + 1 ms
    /// floods the port hundreds of times a second, far above the 200 ms rate at
    /// which the coordinator/UI actually consume LastMeasurement.</param>
    public BusPoller(string name, IReadOnlyList<IBaseComponent> devices,
        Action? prePass = null, int minCyclePeriodMs = 0)
    {
        Name = name;
        _devices = devices;
        _prePass = prePass;
        _minCyclePeriodMs = minCyclePeriodMs;
    }

    /// <summary>UTC ticks of the last completed cycle. Read with Volatile.Read.</summary>
    public long LastTickUtc => Volatile.Read(ref _lastTickUtc);

    public Task Run(CancellationToken ct)
    {
        // A fully SYNCHRONOUS loop on a dedicated LongRunning thread. It must not
        // contain any `await`: an await would resume continuations (and the next
        // device.Poll) on a thread-pool thread, defeating the off-pool isolation —
        // LongRunning only covers the synchronous prefix up to the first await.
        return Task.Factory.StartNew(() =>
        {
            var cycle = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested)
            {
                cycle.Restart();
                try
                {
                    // Poll directly on this poller's own dedicated thread — do NOT offload
                    // reads to the thread pool. Each bus owns its thread and serial reads
                    // are bounded by SerialPort.ReadTimeout, so a slow device only delays
                    // its own bus; a fully wedged bus is recovered by the per-bus respawn
                    // watchdog. Offloading every device read to Task.Run (one pool thread
                    // per bus, plus the nested serial read task) starved the thread pool
                    // with one bus poller per serial device, stalling operator commands for
                    // tens of seconds and canceling their reads.
                    if (_prePass is not null)
                        _prePass();

                    foreach (var device in _devices)
                    {
                        if (ct.IsCancellationRequested) break;
                        try
                        {
                            device.Poll();
                        }
                        catch (Exception ex)
                        {
                            // Isolate per device: one transient read fault must not skip
                            // the rest of the cycle. The driver updates its own
                            // IsResponding / Last* state on failure.
                            Trace.TraceWarning(
                                $"BusPoller[{Name}] device {device.Params.ID} poll error: {ex.Message}");
                        }
                    }
                    Volatile.Write(ref _lastTickUtc, DateTime.UtcNow.Ticks);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"BusPoller[{Name}] cycle error: {ex.Message}");
                }

                // Pace the cycle to _minCyclePeriodMs (wall-clock), waiting out only
                // the time the reads didn't already consume. Floored at 1 ms so a hot
                // bus does not spin a core. WaitOne on the token's handle is a cancelable
                // sleep that stays on this thread and wakes immediately on cancellation.
                if (ct.IsCancellationRequested) break;
                int remaining = _minCyclePeriodMs - (int)cycle.ElapsedMilliseconds;
                try { ct.WaitHandle.WaitOne(Math.Max(1, remaining)); }
                catch (ObjectDisposedException) { break; }
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }
}
