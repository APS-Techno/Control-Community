using APS.Controls.Core;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// A Unicode en-dash in `stage HS –283` was once dropped by the lexer, the line ran as
/// `stage HS 283`, and the substrate arm drove the wrong direction. Embedded recipes run no
/// pre-run validator (ProcessEmbeddedRecipe), so execution itself must refuse a token stream
/// with lexer Diagnostics (audit 2026-08-29, C4).
/// </summary>
public class LexerDiagnosticsExecutionGuardTests
{
    private static (CoreThread Thread, ChannelPool Pool) NewThread(string embedXml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-embed-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, embedXml);
        var pool = new ChannelPool();
        var thread = new CoreThread(pool);
        thread.Core.LogEmbeddedRecipes = true;
        thread.Core.embRecipes = new EmbeddedRecipes(path);
        File.Delete(path);
        return (thread, pool);
    }

    private static List<string> Drain(ChannelPool pool, out List<string> dispatched)
    {
        var lines = new List<string>();
        dispatched = new List<string>();
        while (pool.GUIReader!.TryRead(out var msg))
        {
            if (msg is ValMsg<string> { Value: not null } v)
            {
                lines.Add(v.Value);
                if (v.Value.Contains("exec:"))
                    dispatched.Add(v.Value[(v.Value.IndexOf("exec:") + 5)..].Trim());
            }
        }
        return lines;
    }

    [Fact]
    public async Task Embedded_recipe_with_stray_character_is_refused_not_mutilated()
    {
        // en-dash (U+2013) where a minus sign belongs — the incident character.
        var (thread, pool) = NewThread("""
            <?xml version="1.0" encoding="utf-8"?>
            <recipes>
              <recipe id="MOVE_ARM">
                <l>stage HS –283</l>
              </recipe>
            </recipes>
            """);

        await thread.ProcessEmbeddedRecipe("MOVE_ARM");
        var log = Drain(pool, out var dispatched);

        // Nothing may execute — the alternative is `stage HS 283`, reversed motion.
        Assert.Empty(dispatched);
        Assert.Contains(log, l => l.Contains('–') && l.Contains("not part of any recipe token"));
    }

    [Fact]
    public async Task Call_child_with_stray_character_is_refused()
    {
        var (thread, pool) = NewThread("""
            <?xml version="1.0" encoding="utf-8"?>
            <recipes>
              <recipe id="CALLER">
                <l>call CHILD</l>
              </recipe>
              <recipe id="CHILD">
                <l>stage HS –283</l>
              </recipe>
            </recipes>
            """);

        await thread.ProcessEmbeddedRecipe("CALLER");
        var log = Drain(pool, out var dispatched);

        Assert.Empty(dispatched);
        Assert.Contains(log, l => l.Contains('–') && l.Contains("not part of any recipe token"));
    }

    [Fact]
    public async Task Clean_recipe_still_executes()
    {
        var (thread, pool) = NewThread("""
            <?xml version="1.0" encoding="utf-8"?>
            <recipes>
              <recipe id="STEP" onStepFailure="continue">
                <l>valve A1 open</l>
              </recipe>
            </recipes>
            """);

        await thread.ProcessEmbeddedRecipe("STEP");
        Drain(pool, out var dispatched);

        Assert.Single(dispatched);
    }
}
