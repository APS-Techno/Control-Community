using APS.SystemControl.Core.Classes;

namespace APS.SystemControl.Tests.Overrides;

public class RegistryStateTests
{
    private static OverrideRegistry NewRegistry(out FakeLogWriter log)
    {
        log = new FakeLogWriter();
        return new OverrideRegistry(log, clock: () => new DateTime(2026, 4, 13, 12, 0, 0));
    }

    [Fact]
    public void Fresh_Registry_Reports_Nothing_Overridden()
    {
        var r = NewRegistry(out _);
        Assert.False(r.IsAlarmOverridden("alarm:pl|Processing|>|0.05"));
        Assert.False(r.IsInterlockOverridden("valve_m1_true"));
        Assert.Equal(0, r.ActiveCount);
    }

    [Fact]
    public void SetAlarmOverride_Then_Query_Returns_True()
    {
        var r = NewRegistry(out _);
        r.SetAlarmOverride("alarm:pl|Processing|>|0.05", enabled: true,
            persist: false, reason: "gauge drift", by: "engineer");
        Assert.True(r.IsAlarmOverridden("alarm:pl|Processing|>|0.05"));
        Assert.Equal(1, r.ActiveCount);
    }

    [Fact]
    public void SetAlarmOverride_False_Clears_It()
    {
        var r = NewRegistry(out _);
        r.SetAlarmOverride("id1", true, false, "r", "u");
        r.SetAlarmOverride("id1", false, false, "", "u");
        Assert.False(r.IsAlarmOverridden("id1"));
        Assert.Equal(0, r.ActiveCount);
    }

    [Fact]
    public void SetInterlockOverride_Is_Independent_Of_Alarm_Namespace()
    {
        var r = NewRegistry(out _);
        r.SetInterlockOverride("valve_m1_true", true, false, "r", "u");
        Assert.False(r.IsAlarmOverridden("valve_m1_true"));
        Assert.True(r.IsInterlockOverridden("valve_m1_true"));
    }

    [Fact]
    public void OverridesChanged_Fires_On_Set_And_Clear()
    {
        var r = NewRegistry(out _);
        int fired = 0;
        r.OverridesChanged += (_, _) => fired++;

        r.SetAlarmOverride("id1", true, false, "r", "u");
        r.SetAlarmOverride("id1", false, false, "", "u");

        Assert.Equal(2, fired);
    }

    [Fact]
    public void ClearAll_Removes_Everything()
    {
        var r = NewRegistry(out _);
        r.SetAlarmOverride("a1", true, false, "r", "u");
        r.SetInterlockOverride("i1", true, true, "r", "u");

        r.ClearAll("u");

        Assert.Equal(0, r.ActiveCount);
        Assert.False(r.IsAlarmOverridden("a1"));
        Assert.False(r.IsInterlockOverridden("i1"));
    }

    [Fact]
    public async Task Concurrent_Toggles_And_Queries_Do_Not_Throw()
    {
        var log = new FakeLogWriter();
        var r = new OverrideRegistry(log, clock: () => DateTime.UtcNow);
        var toggler = Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
                r.SetAlarmOverride("a" + (i % 8), i % 2 == 0, false, "r", "u");
        });
        var querier = Task.Run(() =>
        {
            for (int i = 0; i < 10_000; i++)
                _ = r.IsAlarmOverridden("a" + (i % 8));
        });
        await Task.WhenAll(toggler, querier);
    }
}

internal sealed class FakeLogWriter : IOverrideLogWriter
{
    private readonly List<string> _lines = new();
    private readonly object _sync = new();

    public IReadOnlyList<string> Lines
    {
        get { lock (_sync) return _lines.ToArray(); }
    }

    public void Write(string line)
    {
        lock (_sync) _lines.Add(line);
    }

    public void Clear()
    {
        lock (_sync) _lines.Clear();
    }
}
