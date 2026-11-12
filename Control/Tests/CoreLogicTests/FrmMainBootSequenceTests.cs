using APS.SystemControl;
using APS.SystemControl.Hosting;

namespace CoreLogicTests;

public class FrmMainBootSequenceTests
{
    private sealed class RecordingModule(List<string> log) : IControlModule
    {
        public void OnBootComplete(FrmMain form) => log.Add("module.OnBootComplete");
    }

    [Fact]
    public void CompleteBoot_runs_OnBootComplete_after_the_boot_gate_opens()
    {
        var log = new List<string>();
        var modules = new IControlModule[] { new RecordingModule(log) };

        FrmMain.CompleteBoot(
            null!,
            () => log.Add("gate"),
            (_, call) => { foreach (var m in modules) call(m); });

        Assert.Equal(["gate", "module.OnBootComplete"], log);
    }
}
