namespace APS.SystemControl.Core.Evidence;

/// <summary>
/// Accumulates per-channel means and the minimum pressure over a single run, for the
/// snapshot's unsigned outcome summary. Fed from the same per-row value set that the TSV
/// data log writes (<c>LogConfig.CollectValues</c>) so the snapshot's Means mirror the TSV
/// columns exactly — one source of truth, no drift. The full time-series stays in the TSV.
/// Outputs are rounded to 4 significant figures to match the TSV's G4 precision.
///
/// Thread-safe: <see cref="Sample"/> runs on the 200 ms hardware-poll thread while
/// <see cref="Means"/>/<see cref="BasePressureOrNull"/> are read at run finalize on the UI
/// thread; all access is guarded by <c>_lock</c>.
/// </summary>
public sealed class RunActualsAccumulator
{
    private readonly object _lock = new();
    private readonly Dictionary<string, (double Sum, int N)> _means = new(StringComparer.Ordinal);
    private string? _pressureChannel;
    private string? _pressureUnit;
    private double _pressureMin = double.PositiveInfinity;

    public void ConfigurePressure(string channel, string unit)
    {
        lock (_lock)
        {
            _pressureChannel = channel;
            _pressureUnit = unit;
        }
    }

    public void Sample(string id, double value)
    {
        lock (_lock)
        {
            var (sum, n) = _means.TryGetValue(id, out var e) ? e : (0d, 0);
            _means[id] = (sum + value, n + 1);
            if (id == _pressureChannel && value < _pressureMin) _pressureMin = value;
        }
    }

    public Dictionary<string, double> Means()
    {
        lock (_lock)
            return _means.ToDictionary(kvp => kvp.Key, kvp => Sig4(kvp.Value.Sum / kvp.Value.N));
    }

    public BasePressure? BasePressureOrNull()
    {
        lock (_lock)
            return double.IsFinite(_pressureMin) && _pressureChannel is not null
                ? new BasePressure { Channel = _pressureChannel, Min = Sig4(_pressureMin), Unit = _pressureUnit }
                : null;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _means.Clear();
            _pressureMin = double.PositiveInfinity;
        }
    }

    /// <summary>Round to 4 significant figures — the numeric equivalent of the TSV's
    /// G4 formatting (~0.1% relative), so the snapshot summary carries the same
    /// precision and self-scales from base pressure (~1e-7) to hundreds of volts.</summary>
    private static double Sig4(double v)
    {
        if (v == 0 || !double.IsFinite(v)) return v;
        int power = 4 - (int)Math.Ceiling(Math.Log10(Math.Abs(v)));
        double mag = Math.Pow(10, power);
        return Math.Round(v * mag) / mag;
    }
}
