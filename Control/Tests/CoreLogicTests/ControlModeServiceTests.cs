using APS.SystemControl.Core;
using Xunit;

namespace CoreLogicTests;

public class ControlModeServiceTests
{
    private static ControlModeService Svc(ControlMode mode, List<string>? log = null)
    {
        var s = new ControlModeService();
        s.Initialize(new ControlModeStartup(null, false, mode.ToString(), log is null ? null : log.Add));
        return s;
    }

    [Theory]
    [InlineData(ControlMode.Offline, CommandOrigin.Host, CommandClass.Read, null)]
    [InlineData(ControlMode.Local,   CommandOrigin.Host, CommandClass.Read, null)]
    [InlineData(ControlMode.Remote,  CommandOrigin.Host, CommandClass.Read, null)]
    [InlineData(ControlMode.Offline, CommandOrigin.Host, CommandClass.Mutate, "control mode is Offline — host commands need Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the operator")]
    [InlineData(ControlMode.Local,   CommandOrigin.Host, CommandClass.Mutate, "control mode is Local — host commands need Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the operator")]
    [InlineData(ControlMode.Remote,  CommandOrigin.Host, CommandClass.Mutate, null)]
    [InlineData(ControlMode.Local,   CommandOrigin.Host, CommandClass.Safety, "control mode is Local — host commands need Online-Remote; only an operator can switch it (Service → Control mode…) — do not poll, ask the operator")]
    [InlineData(ControlMode.Offline, CommandOrigin.Operator, CommandClass.Mutate, null)]
    [InlineData(ControlMode.Local,   CommandOrigin.Operator, CommandClass.Mutate, null)]
    [InlineData(ControlMode.Remote,  CommandOrigin.Operator, CommandClass.Mutate, "control mode is Remote — operator commands are locked; switch to Local")]
    [InlineData(ControlMode.Remote,  CommandOrigin.Operator, CommandClass.Safety, null)]
    [InlineData(ControlMode.Remote,  CommandOrigin.Operator, CommandClass.Read, null)]
    public void Rule_table(ControlMode mode, CommandOrigin origin, CommandClass cls, string? expected)
    {
        Assert.Equal(expected, Svc(mode).Authorize(origin, cls));
    }

    [Fact]
    public void Default_is_Local_and_Set_raises_Changed_and_logs()
    {
        var log = new List<string>();
        var s = new ControlModeService();
        s.Initialize(new ControlModeStartup(null, false, null, log.Add));
        Assert.Equal(ControlMode.Local, s.Mode);
        Assert.Contains(log, l => l.StartsWith("[ControlMode] startup mode Local"));

        ControlModeChange? seen = null;
        s.Changed += (_, c) => seen = c;
        Assert.True(s.Set(ControlMode.Remote, "alex"));
        Assert.True(s.IsRemote);
        Assert.Equal((ControlMode.Local, ControlMode.Remote, "alex"), (seen!.From, seen.To, seen.By));
        Assert.Same(seen, s.LastChange);
        Assert.Contains("[ControlMode] Local → Remote by alex", log);

        seen = null;
        Assert.False(s.Set(ControlMode.Remote, "alex"));      // idempotent: no event, no log line
        Assert.Null(seen);
        Assert.Equal(1, log.Count(l => l.Contains("→")));
    }

    [Fact]
    public void Set_persists_and_restoreRemote_restores_only_Remote()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cm-{Guid.NewGuid():N}.state");
        try
        {
            var s = new ControlModeService();
            s.Initialize(new ControlModeStartup(path, true, null, null));
            s.Set(ControlMode.Remote, "t");
            Assert.Equal("Remote", File.ReadAllText(path).Trim());

            Assert.Equal(ControlMode.Remote, ControlModeService.ResolveStartup(new(path, true, null, null), out var why));
            Assert.Contains("restoreRemote", why);
            Assert.Equal(ControlMode.Local, ControlModeService.ResolveStartup(new(path, false, null, null), out _));

            s.Set(ControlMode.Offline, "t");
            Assert.Equal(ControlMode.Local, ControlModeService.ResolveStartup(new(path, true, null, null), out _));   // Offline never restored

            File.WriteAllText(path, "garbage");
            Assert.Equal(ControlMode.Local, ControlModeService.ResolveStartup(new(path, true, null, null), out _));
            File.Delete(path);
            Assert.Equal(ControlMode.Local, ControlModeService.ResolveStartup(new(path, true, null, null), out _));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void Dev_override_wins_and_bad_override_falls_back_to_Local()
    {
        string path = Path.Combine(Path.GetTempPath(), $"cm-{Guid.NewGuid():N}.state");
        File.WriteAllText(path, "Remote");
        try
        {
            Assert.Equal(ControlMode.Offline, ControlModeService.ResolveStartup(new(path, true, "offline", null), out var why));
            Assert.Contains("APS_DEV_CONTROL_MODE", why);
            Assert.Equal(ControlMode.Local, ControlModeService.ResolveStartup(new(path, true, "bogus", null), out why));
            Assert.Contains("bogus", why);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Unwritable_persist_path_does_not_block_the_change_and_warns_once()
    {
        var log = new List<string>();
        var s = new ControlModeService();
        s.Initialize(new ControlModeStartup(Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid().ToString("N"), "x.state"), false, null, log.Add));
        Assert.True(s.Set(ControlMode.Remote, "t"));
        Assert.True(s.Set(ControlMode.Local, "t"));
        Assert.Equal(1, log.Count(l => l.Contains("cannot persist")));
    }
}
