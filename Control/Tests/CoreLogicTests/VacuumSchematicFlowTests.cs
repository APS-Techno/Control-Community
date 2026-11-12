using System.Linq;
using APS.Controls.XML.GUI;
using APS.SystemControl.Controls.Vacuum;
using Xunit;

namespace CoreLogicTests;

/// <summary>
/// Flow-propagation semantics for the runtime vacuum schematic, focused on the
/// throttle-valve conduit rule: a throttle conducts flow when open but never
/// originates it, so an open throttle between two closed valves (nothing running)
/// must leave its pipes idle instead of highlighting both sides.
/// </summary>
public class VacuumSchematicFlowTests
{
    private static XML_Schematic Schem(params XML_Pipe[] pipes) =>
        new() { Pipes = new XML_Pipes { Items = pipes.ToList() } };

    private static XML_Pipe Pipe(string id, string from, string to) =>
        new() { Id = id, From = from, To = to };

    [Fact]
    public void OpenThrottle_BetweenClosedValves_NothingRunning_DoesNotFlow()
    {
        using var sch = new VacuumSchematic();
        sch.LoadFromXml(Schem(
            Pipe("pIn", "V1:out", "TH:top"),
            Pipe("pOut", "TH:bottom", "V2:in")));

        sch.RegisterThrottleComponent("TH");
        sch.SetComponentOpen("TH", true);   // throttle open (conduit); V1/V2 stay closed
        sch.RecalculateFlow();

        Assert.False(sch.IsPipeFlowing("pIn"));
        Assert.False(sch.IsPipeFlowing("pOut"));
    }

    [Fact]
    public void OpenThrottle_WithRunningPumpUpstream_ConductsToBothSides()
    {
        using var sch = new VacuumSchematic();
        sch.LoadFromXml(Schem(
            Pipe("pIn", "P:out", "TH:top"),
            Pipe("pOut", "TH:bottom", "C:in")));

        sch.RegisterThrottleComponent("TH");
        sch.SetComponentOpen("P", true);    // pump running = real flow source
        sch.SetComponentOpen("TH", true);   // throttle open = conduit
        sch.RecalculateFlow();

        Assert.True(sch.IsPipeFlowing("pIn"));
        Assert.True(sch.IsPipeFlowing("pOut"));
    }

    [Fact]
    public void SetComponentOpen_ReportsWhetherOpenSetChanged()
    {
        using var sch = new VacuumSchematic();
        sch.LoadFromXml(Schem(Pipe("p", "P:out", "C:in")));

        // First open is a change; re-opening the same component is a no-op.
        Assert.True(sch.SetComponentOpen("P", true));
        Assert.False(sch.SetComponentOpen("P", true));

        // Closing it changes the set; closing again is a no-op.
        Assert.True(sch.SetComponentOpen("P", false));
        Assert.False(sch.SetComponentOpen("P", false));
    }

    [Fact]
    public void ClosedThrottle_BlocksFlowPastIt()
    {
        using var sch = new VacuumSchematic();
        sch.LoadFromXml(Schem(
            Pipe("pIn", "P:out", "TH:top"),
            Pipe("pOut", "TH:bottom", "C:in")));

        sch.RegisterThrottleComponent("TH");
        sch.SetComponentOpen("P", true);    // pump running
        sch.SetComponentOpen("TH", false);  // throttle closed = block
        sch.RecalculateFlow();

        Assert.True(sch.IsPipeFlowing("pIn"));    // pipe touching the running pump
        Assert.False(sch.IsPipeFlowing("pOut"));  // dead-ended by the closed throttle
    }
}
