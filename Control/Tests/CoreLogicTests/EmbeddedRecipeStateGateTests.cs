using APS.Controls.Core;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// notIn="…" on an embedded recipe: the chamber states in which a button / host launch is
/// refused. The single-flight slot only blocks while a recipe RUNS; the main chamber stays
/// in Processing after a run for as long as gas is still flowing (Alarms.xml gas group), and in
/// that window SYSTEM_AIR_RELEASE or LOAD_SUBSTRATE would vent, or open GVL into, a chamber
/// that is still processing. Only pump-out and the load-lock operations may run then.
/// Nested 'call' goes through the interpreter and is not gated.
/// </summary>
public class EmbeddedRecipeStateGateTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="utf-8"?>
        <recipes>
          <recipe id="VENT" notIn="processing">
            <l>MFC Ar1 0</l>
          </recipe>
          <recipe id="PUMP">
            <l>MFC Ar1 0</l>
          </recipe>
          <recipe id="MULTI" notIn="Processing, maintenance">
            <l>MFC Ar1 0</l>
          </recipe>
          <recipe id="TYPO" notIn="procesing">
            <l>MFC Ar1 0</l>
          </recipe>
        </recipes>
        """;

    private static (CoreThread Thread, ChannelPool Pool) NewThread()
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, Xml);
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;
        thread.Core.embRecipes = new EmbeddedRecipes(path);
        File.Delete(path);
        return (thread, pool);
    }

    private static List<string> Drain(ChannelPool pool)
    {
        var lines = new List<string>();
        while (pool.GUIReader!.TryRead(out var msg))
            if (msg is ValMsg<string> { Value: not null } v) lines.Add(v.Value);
        return lines;
    }

    [Fact]
    public async Task Listed_state_refuses_the_launch_without_claiming_the_slot()
    {
        var (thread, pool) = NewThread();
        thread.Core.SystemState = SystemState.Processing;
        Drain(pool);

        await thread.ProcessEmbeddedRecipe("VENT");
        var log = Drain(pool);

        Assert.DoesNotContain(log, l => l.Contains("Recipe started: VENT"));
        Assert.Contains(log, l => l.Contains("VENT") && l.Contains("Processing"));
        Assert.False(thread.Core.IsRecipeRunning);
    }

    [Fact]
    public async Task Unlisted_recipe_still_runs_in_that_state()
    {
        var (thread, pool) = NewThread();
        thread.Core.SystemState = SystemState.Processing;
        Drain(pool);

        await thread.ProcessEmbeddedRecipe("PUMP");

        Assert.Contains(Drain(pool), l => l.Contains("Recipe started: PUMP"));
    }

    [Fact]
    public async Task Listed_recipe_runs_in_every_other_state()
    {
        var (thread, pool) = NewThread();
        thread.Core.SystemState = SystemState.Idle;
        Drain(pool);

        await thread.ProcessEmbeddedRecipe("VENT");

        Assert.Contains(Drain(pool), l => l.Contains("Recipe started: VENT"));
    }

    [Fact]
    public async Task Several_states_and_any_case_are_accepted()
    {
        var (thread, pool) = NewThread();
        thread.Core.SystemState = SystemState.Maintenance;
        Drain(pool);

        await thread.ProcessEmbeddedRecipe("MULTI");

        Assert.DoesNotContain(Drain(pool), l => l.Contains("Recipe started: MULTI"));
    }

    [Fact]
    public void Parsed_states_are_exposed_for_the_panel_buttons()
    {
        var recipes = NewThread().Thread.Core.embRecipes!;
        Assert.True(recipes.IsBlockedIn("VENT", SystemState.Processing));
        Assert.False(recipes.IsBlockedIn("VENT", SystemState.Idle));
        Assert.True(recipes.IsBlockedIn("MULTI", SystemState.Maintenance));
        Assert.False(recipes.IsBlockedIn("PUMP", SystemState.Processing));
        Assert.False(recipes.IsBlockedIn("NO_SUCH_RECIPE", SystemState.Processing));
    }

    [Fact]
    public void Unknown_state_name_is_reported_not_silently_bound()
    {
        var recipes = NewThread().Thread.Core.embRecipes!;
        Assert.Contains(recipes.ConfigErrors, e => e.Contains("TYPO") && e.Contains("procesing"));
        Assert.False(recipes.IsBlockedIn("TYPO", SystemState.Processing));
    }
}
