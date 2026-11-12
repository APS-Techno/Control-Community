namespace APS.SystemControl.GUIControls.Process;

/// <summary>
/// Single-threaded use only (UI thread) — no locking.
/// </summary>
internal sealed class ReadingRingBuffer
{
    private readonly float[] _buffer;
    private int _head;    // index of next write
    private int _count;

    public ReadingRingBuffer(int capacity)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;
    public int Count => _count;

    public void Add(float value)
    {
        _buffer[_head] = value;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length) _count++;
    }

    public void Clear()
    {
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// Copies in chronological order (oldest first). Returns the number of samples written.
    /// </summary>
    public int CopyTo(float[] dest)
    {
        if (dest.Length < _count) throw new ArgumentException("dest too small");
        int start = (_head - _count + _buffer.Length) % _buffer.Length;
        for (int i = 0; i < _count; i++)
            dest[i] = _buffer[(start + i) % _buffer.Length];
        return _count;
    }

    /// <summary>
    /// Min / max over the samples greater than zero, for readings plotted on a
    /// log axis. A gauge reports exactly 0 while it is off or below range, and
    /// such a sample has no place on a log axis — including it in the minimum
    /// would drag the axis to negative infinity. Returns (0, 0) when no sample
    /// is positive (the whole window is "off").
    /// </summary>
    public (float Min, float Max) MinMaxPositive()
    {
        int start = (_head - _count + _buffer.Length) % _buffer.Length;
        float min = 0, max = 0;
        bool any = false;

        for (int i = 0; i < _count; i++)
        {
            float v = _buffer[(start + i) % _buffer.Length];
            if (!(v > 0)) continue;         // also skips NaN
            if (!any) { min = max = v; any = true; continue; }
            if (v < min) min = v;
            else if (v > max) max = v;
        }

        return (min, max);
    }

    /// <summary>Returns (0, 0) when empty.</summary>
    public (float Min, float Max) MinMax()
    {
        if (_count == 0) return (0, 0);
        int start = (_head - _count + _buffer.Length) % _buffer.Length;
        float min = _buffer[start], max = min;
        for (int i = 1; i < _count; i++)
        {
            float v = _buffer[(start + i) % _buffer.Length];
            if (v < min) min = v;
            else if (v > max) max = v;
        }
        return (min, max);
    }
}
