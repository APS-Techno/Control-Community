using APS.ProcessPanel.Contracts;
using APS.SystemControl;
using Xunit;

namespace CoreLogicTests;

public class ProcessTelemetryCollectorTests
{
    [Fact]
    public void MarkOfflineByDevice_clears_the_device_and_its_derived_ids_only()
    {
        var c = new ProcessTelemetryCollector();
        c.RegisterMany(["PSU-A", "PSU-A_V", "PSU-A_I", "PSU-AX_V", "PSU-B_V"]);
        foreach (var id in new[] { "PSU-A", "PSU-A_V", "PSU-A_I", "PSU-AX_V", "PSU-B_V" })
            c.Push(id, 1.0, ReadingState.Ok);

        c.MarkOfflineByDevice("PSU-A");

        var snap = c.Snapshot().ToDictionary(r => r.Id);
        Assert.Equal(ReadingState.Off, snap["PSU-A"].State);
        Assert.Null(snap["PSU-A"].Value);
        Assert.Equal(ReadingState.Off, snap["PSU-A_V"].State);
        Assert.Equal(ReadingState.Off, snap["PSU-A_I"].State);
        Assert.Equal(ReadingState.Ok,  snap["PSU-AX_V"].State);   // prefix without '_' is a different device
        Assert.Equal(ReadingState.Ok,  snap["PSU-B_V"].State);
    }

    [Fact]
    public void MarkOfflineByDevice_null_is_a_no_op()
    {
        var c = new ProcessTelemetryCollector();
        c.Register("a");
        c.Push("a", 2.0, ReadingState.Ok);
        c.MarkOfflineByDevice(null);
        Assert.Equal(ReadingState.Ok, c.Snapshot().Single().State);
    }
}
