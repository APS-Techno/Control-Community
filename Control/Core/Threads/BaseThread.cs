namespace APS.SystemControl.Core;

public class BaseThread
{
    private readonly Thread _thread;
    protected ChannelPool pool;

    protected BaseThread(ChannelPool pool)
    {
        this.pool = pool;
        _thread = new Thread(new ThreadStart(RunThread))
        {
            // Background so a blocked or looping control thread can never keep
            // the process alive after the UI closes. CoreThread.RunThread is a
            // synchronous loop that blocks on the HW channel until it is
            // completed; without this flag the foreground thread stayed parked
            // in WaitToReadAsync and the process never exited once the main
            // form closed. Graceful shutdown (CoreThread.Stop) is the deterministic
            // path; IsBackground is the safety net.
            IsBackground = true
        };
    }

    protected virtual void RunThread() { }

    public void Start() => _thread.Start();
    public void Join() => _thread.Join();
    public bool Join(int millisecondsTimeout) => _thread.Join(millisecondsTimeout);
    public bool IsAlive => _thread.IsAlive;

}
