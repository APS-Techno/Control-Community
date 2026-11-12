namespace APS.Wire;

public sealed class BackoffPolicy
{
    private readonly int _baseMs;
    private readonly int _maxMs;
    private int _failures;

    public BackoffPolicy(int baseMs, int maxMs)
    {
        _baseMs = baseMs <= 0 ? 1 : baseMs;
        _maxMs = maxMs < _baseMs ? _baseMs : maxMs;
    }

    public bool IsFailing => _failures > 0;

    public void OnSuccess() => _failures = 0;

    public void OnFailure()
    {
        if (_failures < 30) _failures++;
    }

    public int CurrentDelayMs
    {
        get
        {
            if (_failures == 0) return _baseMs;
            int shift = System.Math.Min(_failures, 5);     // cap the shift to avoid overflow
            long delay = (long)_baseMs << shift;
            return (int)System.Math.Min(delay, _maxMs);
        }
    }
}
