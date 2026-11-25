namespace APS.SystemEmulator.Engine;

public class TimeManager
{
    private bool _running;

    public float SimTime { get; private set; }
    public float SpeedMultiplier { get; set; } = 1.0f;
    public bool IsPaused { get; set; }

    public void Start() => _running = true;
    public void Stop() => _running = false;

    public void SetSimTime(float simTime)
    {
        if (!float.IsFinite(simTime) || simTime < 0f)
            throw new ArgumentOutOfRangeException(nameof(simTime), "SimTime must be finite and nonnegative.");
        SimTime = simTime;
    }

    public void Reset()
    {
        SimTime = 0f;
        _running = false;
    }

    public float Advance(float dt)
    {
        if (!_running || IsPaused) return 0f;
        float actual = dt * SpeedMultiplier;
        SimTime += actual;
        return actual;
    }
}
