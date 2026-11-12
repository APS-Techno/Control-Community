using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Starting a recipe must not touch the wire. Everything in ProcessEmbeddedRecipe
/// before its first await runs on the caller's thread, so a live
/// <c>BaseSensor.Value()</c> read there (it takes the lock the bus poller holds and
/// has no <c>Connected</c> guard) can sit on a dead socket until TCP gives up,
/// blocking the HW message pump — including the Abort command dispatched at the top
/// of HWDispatcher. Callers outside the poll thread read
/// <see cref="BaseSensor.LastValue"/>.
/// </summary>
public class RecipeStartupLatencyTests
{
    /// <summary>
    /// A sensor whose live read can be armed to fail. Seeded once so
    /// <see cref="BaseSensor.LastValue"/> holds a real number, exactly as the
    /// bus poller would leave it.
    /// </summary>
    private sealed class FakeSensor : BaseSensor
    {
        private readonly float _seed;
        public int LiveReads;
        public bool FailOnRead;

        public FakeSensor(float seed, bool connected = true)
            : base(new BaseDeviceParams("pm", 0, "pm"))
        {
            _seed = seed;
            Connected = true;
            Value();            // seed LastValue the way a poll cycle does
            LiveReads = 0;      // don't count the seeding read
            Connected = connected;
        }

        protected override float ReadHW()
        {
            LiveReads++;
            if (FailOnRead)
                throw new InvalidOperationException(
                    "live sensor read on the recipe-start path (this is the 2026-08-04 stall)");
            return _seed;
        }
    }

    private const string OneLiner = """
        <?xml version="1.0" encoding="utf-8"?>
        <recipes>
          <recipe id="TRIVIAL">
            <l>valve A1 open</l>
          </recipe>
        </recipes>
        """;

    private static (CoreThread Thread, ChannelPool Pool, FakeSensor Sensor) NewThread(
        float seed = 1.5e-6f, bool connected = true)
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;

        var sensor = new FakeSensor(seed, connected) { FailOnRead = true };
        thread.Core.hwElements.Item[DeviceType.Sensor].Add("pm", sensor);

        return (thread, pool, sensor);
    }

    private static List<string> Drain(ChannelPool pool)
    {
        var lines = new List<string>();
        while (pool.GUIReader!.TryRead(out var msg))
            if (msg is ValMsg<string> { Value: not null } v) lines.Add(v.Value);
        return lines;
    }

    private static async Task<List<string>> RunTrivialRecipe(CoreThread thread, ChannelPool pool)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, OneLiner);
        try
        {
            thread.Core.embRecipes = new EmbeddedRecipes(path);
            await thread.ProcessEmbeddedRecipe("TRIVIAL");
            return Drain(pool);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// The base-pressure line must come from the poll cache. The fake throws on
    /// any live read, so if the recipe-start path still calls Value() the run
    /// stalls-then-fails instead of logging a pressure.
    /// </summary>
    [Fact]
    public async Task Base_pressure_comes_from_the_poll_cache_not_the_wire()
    {
        var (thread, pool, sensor) = NewThread(seed: 1.5e-6f);

        var log = await RunTrivialRecipe(thread, pool);

        Assert.Equal(0, sensor.LiveReads);
        Assert.Contains(log, l => l.StartsWith("Base pressure:") && l.Contains("1.50E-006"));
    }

    /// <summary>
    /// A disconnected sensor must say so rather than reporting a fabricated 0.
    /// </summary>
    [Fact]
    public async Task Base_pressure_says_unavailable_when_the_sensor_is_disconnected()
    {
        var (thread, pool, sensor) = NewThread(seed: 1.5e-6f, connected: false);

        var log = await RunTrivialRecipe(thread, pool);

        Assert.Equal(0, sensor.LiveReads);
        Assert.Contains(log, l => l.StartsWith("Base pressure:") && l.Contains("unavailable"));
        Assert.DoesNotContain(log, l => l.StartsWith("Base pressure:") && l.Contains("0.00E"));
    }

    /// <summary>
    /// The recipe still starts. Guards against "fixing" the stall by removing
    /// the base-pressure log or bailing out of startup early.
    /// </summary>
    [Fact]
    public async Task Recipe_still_starts_and_runs_its_step()
    {
        var (thread, pool, _) = NewThread();

        var log = await RunTrivialRecipe(thread, pool);

        Assert.Contains(log, l => l.StartsWith("Recipe started: TRIVIAL"));
        Assert.Contains(log, l => l.Contains("exec: valve A1 true"));
    }

    /// <summary>
    /// Startup latency is reported every run, so the next time something on this
    /// path starts blocking it shows up as a number in the operator log instead
    /// of as an unexplained dead UI.
    /// </summary>
    [Fact]
    public async Task Recipe_start_latency_is_logged()
    {
        var (thread, pool, _) = NewThread();

        var log = await RunTrivialRecipe(thread, pool);

        string latency = Assert.Single(log, l => l.StartsWith("Recipe start latency:"));
        Assert.Matches(@"^Recipe start latency: \d+ ms", latency);
        // Startup is bookkeeping plus one cached read; it has no business
        // being slow. If this ever trips, the path went back to doing I/O.
        Assert.DoesNotContain("slow", latency);
    }

    /// <summary>
    /// The base-pressure lookup must not throw the run away if the sensor is
    /// missing entirely (profiles that don't define "pm").
    /// </summary>
    [Fact]
    public async Task Missing_sensor_does_not_break_recipe_start()
    {
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;   // no "pm" registered at all

        var log = await RunTrivialRecipe(thread, pool);

        Assert.Contains(log, l => l.StartsWith("Recipe started: TRIVIAL"));
    }
}
