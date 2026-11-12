namespace APS.RecipeModel.Blocks;

/// <summary>When the interval elapses without another <see cref="Ping"/>, <see cref="Elapsed"/>
/// is raised once, on the UI thread.</summary>
public sealed class DebounceTimer : IDisposable
{
    private readonly System.Windows.Forms.Timer _timer;

    public event Action? Elapsed;

    public DebounceTimer(int intervalMs)
    {
        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            Elapsed?.Invoke();
        };
    }

    public void Ping()
    {
        _timer.Stop();
        _timer.Start();
    }

    public void Cancel() => _timer.Stop();

    public void Dispose() => _timer.Dispose();
}
