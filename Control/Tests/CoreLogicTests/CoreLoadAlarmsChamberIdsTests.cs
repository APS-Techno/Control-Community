using System.Threading.Channels;
using APS.SystemControl.Core;

namespace CoreLogicTests;

public class CoreLoadAlarmsChamberIdsTests : IDisposable
{
    private readonly string _tmp;

    public CoreLoadAlarmsChamberIdsTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "core-chamber-ids-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, true); } catch { /* best-effort */ }
    }

    private string WriteFile(string name, string content)
    {
        var path = Path.Combine(_tmp, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static APS.SystemControl.Core.Core NewCore() =>
        new(Channel.CreateUnbounded<ThreadMsg>().Writer);

    [Fact]
    public void Flat_config_yields_Main_only_and_no_auto_fallback()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <states initial="startup" />
            </alarms>
            """);

        var core = NewCore();
        Assert.True(core.LoadAlarms(path));

        Assert.False(core.MainIsAutoFallback);
        Assert.Equal(new[] { CoreConstants.DefaultChamberId },
                     core.ChamberIds.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Explicit_Main_chamber_yields_Main_only_and_no_auto_fallback()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber id="Main">
                <states initial="startup" />
              </chamber>
            </alarms>
            """);

        var core = NewCore();
        Assert.True(core.LoadAlarms(path));

        Assert.False(core.MainIsAutoFallback);
        Assert.Equal(new[] { "Main" },
                     core.ChamberIds.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Chambers_without_Main_set_MainIsAutoFallback_and_include_orphan_Main()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber id="TM"><states initial="startup" /></chamber>
              <chamber id="LL"><states initial="startup" /></chamber>
              <chamber id="C1"><states initial="startup" /></chamber>
            </alarms>
            """);

        var core = NewCore();
        Assert.True(core.LoadAlarms(path));

        Assert.True(core.MainIsAutoFallback);
        Assert.Equal(new[] { "C1", "LL", "Main", "TM" },
                     core.ChamberIds.OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Chambers_with_Main_alongside_others_clear_MainIsAutoFallback()
    {
        string path = WriteFile("Alarms.xml",
            """
            <?xml version="1.0" encoding="utf-8"?>
            <alarms>
              <chamber id="Main"><states initial="startup" /></chamber>
              <chamber id="TM"><states initial="startup" /></chamber>
            </alarms>
            """);

        var core = NewCore();
        Assert.True(core.LoadAlarms(path));

        Assert.False(core.MainIsAutoFallback);
        Assert.Equal(new[] { "Main", "TM" },
                     core.ChamberIds.OrderBy(s => s).ToArray());
    }
}
