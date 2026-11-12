using System.IO;
using APS.SystemControl.Core.CoreClass;

namespace APS.SystemControl.Core;

partial class CoreThread : BaseThread
{
    private readonly Core core;
    public string LastErrorMessage => core!.LastErrorMessage;

    public Core Core => core;

    /// <summary>
    /// Called from the UI thread to acknowledge a recipe 'message' command.
    /// Unblocks the recipe execution that is paused waiting for operator input.
    /// </summary>
    public void AcknowledgeMessage() => core.AcknowledgeMessage();

    /// <summary>Called from the UI thread with the operator's fault-pause decision.</summary>
    public void ResolveFault(FaultDecision decision) => core.ResolveFault(decision);

    public CoreThread(ChannelPool Pool) : base(Pool)
    {
        core = new Core(pool.GUIWriter);
    }

    /// <summary>
    /// Graceful shutdown for application exit. Stops the background HW poller
    /// and watchdog, then completes the HW channel so RunThread's blocking read
    /// loop returns and the dedicated thread finishes, and joins it (bounded).
    /// BaseThread runs as a background thread, so even if the join times out the
    /// process can still exit; the join just lets an in-flight dispatch drain.
    /// </summary>
    public void Stop(int joinTimeoutMs = 2000)
    {
        core.StopHwStateReader();
        pool.CompleteHW();
        Join(joinTimeoutMs);
    }

    /// <summary>
    /// Runs the HW command loop on the dedicated thread. Synchronous (NOT
    /// async void): an async-void method on a ThreadStart resumes its
    /// continuations on thread-pool threads with no enclosing handler, so any
    /// exception escaping HWDispatcher (an unguarded cast / Convert / driver
    /// call on malformed panel/remote/recipe input or a serial/PLC fault) would
    /// be unobserved, reach AppDomain.UnhandledException, and tear down the
    /// entire safety-critical control loop — which the HW watchdog does not
    /// respawn. We block on the channel on this dedicated thread and isolate
    /// every message dispatch so one bad command logs and the loop continues.
    /// </summary>
    protected override void RunThread()
    {
        core.StartHwStateReader();

        if (pool.HWReader == null)
            return;

        while (pool.HWReader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
        {
            if (pool.HWReader.TryRead(out var msg))
            {
                try
                {
                    HWDispatcher(msg);
                }
                catch (Exception ex)
                {
                    core.SendErrorMesssage($"HW command dispatch error: {ex.Message}");
                    System.Diagnostics.Trace.TraceError($"CoreThread.HWDispatcher error: {ex}");
                }
            }
        }
    }

    public bool LoadConfig(Config conf, Action<string, int>? progress = null)
    {
        AppPaths.Initialize(conf);
        core!.ApplyConfiguredLogPaths();
        // Seed live recipe-log toggles from RootConfig (App Settings updates them live thereafter).
        core!.LogUserRecipes = conf.LogUserRecipes;
        core!.LogEmbeddedRecipes = conf.LogEmbeddedRecipes;
        bool result = false;

        progress?.Invoke("Loading hardware configuration...", 30);
        if (core!.LoadHWConfigFromFile(conf.HWConfigFile, conf.HwDllBasePath, progress))
        {
            progress?.Invoke("Loading interlocks...", 70);
            if (!core!.LoadInterlocks(conf.InterlocksFile))
            {
                // Fail closed — a deposition controller must not boot with its
                // safety interlocks missing: CheckInterlocks would return null and
                // every guarded valve/vent/pump-off op would run unguarded.
                core!.SendErrorMesssage(
                    $"Safety interlocks failed to load: {core!.LastErrorMessage}. System will not start.");
                return false;
            }
            progress?.Invoke("Loading embedded operations...", 75);
            core!.LoadEmbeddedOperations();
            progress?.Invoke("Loading alarms...", 78);
            if (!core!.LoadAlarms(conf.AlarmFile))
            {
                core!.SendErrorMesssage(
                    $"Alarm configuration failed to load: {core!.LastErrorMessage}. System will not start.");
                return false;
            }
            string overridesFilePath = Path.Combine(conf.ConfigDir, "RuntimeOverrides.xml");
            core!.InitializeOverrides(overridesFilePath);
            progress?.Invoke("Loading recipes...", 82);
            if (core!.LoadWorker(conf.WorkerConfigFile, conf.WorkerDllBasePath))
                result = core!.LoadEmbeddedRecipes(conf.EmbedRecipesFile);
            progress?.Invoke("Loading GUI controls...", 85);
        }
        return result;
    }

    protected void HWDispatcher(ThreadMsg msg)
    {
        if (msg.Type == MsgType.Command && msg.Device == "Abort")
        {
            core.AbortRecipe();
            msg.Dispose();
            return;
        }

        switch (msg.Target)
        {
            case DeviceType.Worker:
                _ = ProcessSingleWorkerCommand(msg);
                break;
            // Off the pump thread deliberately. An async method runs
            // synchronously until its first await, and ProcessEmbeddedRecipe has
            // no await until the recipe's first step — so calling it inline gave
            // recipe startup the power to block this loop. It did: a live PLC
            // read of a dead sensor stalled 5-30 s, and because the
            // Abort branch at the top of this method is dispatched from the same
            // loop, the operator could not stop the machine for that whole time.
            //
            // LogBasePressure no longer does I/O, but the loop must not depend on
            // that staying true — nothing on the far side of this call should be
            // able to wedge Abort. The name is copied out first: msg is disposed
            // as soon as we return.
            case DeviceType.Recipe:
            {
                string recipeName = msg.Device ?? "embedded";
                _ = Task.Run(() => ProcessEmbeddedRecipe(recipeName));
                break;
            }
            case DeviceType.Controller:
                ProcessControllerCommand(msg);
                break;
            case DeviceType.Power:
                ProcessPowerCommand(msg);
                break;
            case DeviceType.Drive:
                ProcessDriveCommand(msg);
                break;
            default:
                break;
        }
        msg.Dispose();
    }
}

