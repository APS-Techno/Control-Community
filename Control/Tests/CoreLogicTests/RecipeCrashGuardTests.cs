using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using APS.Controls.Core;
using APS.SystemControl.BaseHWClasses;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;
using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Core.Recipe;
using APS.SystemControl.GUIControls.General;
using APS.SystemControl.XML.GUI;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Crash guards:
///   C13 — recipe function dispatch shares the 'call' depth budget
///        (MaxCallDepth); unguarded self- or mutually-recursive functions grow
///        the real stack to an uncatchable StackOverflowException that kills
///        the controller mid-run.
///   C17 — the lexer deliberately folds '50mT' into ONE Number token, so
///        'if'/'check' must parse a suffixed RHS; bare float.Parse dies
///        mid-recipe with a raw FormatException.
///   C33 — PanelDragManager.SnapPosition must not pass a negative max to
///        Math.Clamp when the dragged panel is larger than its parent; the
///        ArgumentException re-fires a crash dialog on every mouse move.
/// </summary>
public class RecipeCrashGuardTests
{
    private static (CoreThread Thread, ChannelPool Pool) NewThread(string recipeInnerXml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aps-crash-{Guid.NewGuid():N}.xml");
        File.WriteAllText(path,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<recipes>\n" +
            $"  <recipe id=\"T\">{recipeInnerXml}</recipe>\n</recipes>\n");
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
            if (msg is ValMsg<string> { Value: not null } v)
                lines.Add(v.Value);
        return lines;
    }

    [Fact]
    public async Task Function_chain_deeper_than_the_call_budget_aborts_instead_of_recursing()
    {
        // Ten chained functions — depth 10 exceeds MaxCallDepth (8). A finite
        // chain keeps the RED run from stack-overflowing the test host while
        // still proving the guard: unguarded dispatch runs this to completion.
        var sb = new StringBuilder();
        for (int i = 1; i <= 10; i++)
        {
            sb.Append($"<l>function F{i}</l>");
            if (i < 10) sb.Append($"<l>F{i + 1}</l>");
            sb.Append("<l>endfunction</l>");
        }
        sb.Append("<l>F1</l>");
        var (thread, pool) = NewThread(sb.ToString());

        await thread.ProcessEmbeddedRecipe("T");
        var log = Drain(pool);

        Assert.Contains(log, l => l.Contains("call depth"));
        Assert.DoesNotContain(log, l => l.Contains("Recipe complete"));
    }

    private sealed class FakeSensor : BaseSensor
    {
        public FakeSensor(string id) : base(new BaseDeviceParams(id, 0, "gauge")) { Connected = true; }
        protected override float ReadHW() => 0.001f;   // 1 mTorr
    }

    [Theory]
    [InlineData("50mT", 0.05f)]
    [InlineData("5uT", 0.000005f)]
    [InlineData("1.5", 1.5f)]
    [InlineData("2T", 2f)]
    public void ParseNumberLiteral_accepts_plain_and_suffixed_values(string text, float expected)
        => Assert.Equal(expected, Interpreter.ParseNumberLiteral(text), precision: 7);

    [Fact]
    public void ParseNumberLiteral_rejects_garbage()
        => Assert.ThrowsAny<Exception>(() => Interpreter.ParseNumberLiteral("fast"));

    [Fact]
    public async Task If_condition_accepts_a_suffixed_pressure_literal()
    {
        var (thread, pool) = NewThread("<l>if pm &lt; 50mT</l><l>endif</l>");
        thread.Core.hwElements.Item[DeviceType.Sensor].Add("pm", new FakeSensor("pm"));

        await thread.ProcessEmbeddedRecipe("T");
        var log = Drain(pool);

        Assert.Contains(log, l => l.Contains("Recipe complete"));
    }

    [Fact]
    public void SnapPosition_pins_an_oversized_panel_instead_of_throwing()
    {
        using var parent = new Panel { Size = new Size(400, 300) };
        using var mgr = new PanelDragManager(parent, new XML_Panels(), "unused.xml");

        // Panel larger than the parent: max percent goes negative, and Math.Clamp
        // throws ArgumentException when min > max.
        var p = mgr.SnapPosition(new Point(50, 50), panelWidth: 800, panelHeight: 700);

        Assert.Equal(new Point(0, 0), p);
    }
}
