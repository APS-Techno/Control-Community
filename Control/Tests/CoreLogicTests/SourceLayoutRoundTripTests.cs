using APS.SystemControl.Core.Classes;
using Xunit;

namespace CoreLogicTests;

public class SourceLayoutRoundTripTests
{
    private const string Xml = """
    <?xml version="1.0" encoding="utf-8"?>
    <sources>
      <layout start="90" direction="ccw">
        <chamber id="C1" start="0" direction="cw"/>
      </layout>
      <source flange="1" angle="30"><material>Si</material></source>
      <source flange="2"><material>BN</material></source>
    </sources>
    """;

    private static string WriteTemp(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"src_{Guid.NewGuid():N}.xml");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Parses_default_and_per_chamber_layout_and_angle()
    {
        var path = WriteTemp(Xml);
        try
        {
            var sm = new SourceManager(2);
            sm.Load(path);

            Assert.Equal(new ChamberLayout(90, false), sm.GetLayout(null));   // default (single/fallback)
            Assert.Equal(new ChamberLayout(0, true), sm.GetLayout("C1"));     // per-chamber override
            Assert.Equal(30, sm.GetSource(1)!.AngleDegrees);
            Assert.Null(sm.GetSource(2)!.AngleDegrees);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Absent_layout_yields_defaults()
    {
        var path = WriteTemp("""<?xml version="1.0"?><sources><source flange="1"><material>Si</material></source></sources>""");
        try
        {
            var sm = new SourceManager(1);
            sm.Load(path);
            Assert.Equal(new ChamberLayout(0, true), sm.GetLayout(null));
            Assert.Null(sm.GetSource(1)!.AngleDegrees);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_then_reload_preserves_layout_and_angle()
    {
        var path = WriteTemp(Xml);
        var outPath = WriteTemp("<sources/>");
        try
        {
            var sm = new SourceManager(2);
            sm.Load(path);
            sm.Save(outPath);

            var sm2 = new SourceManager(2);
            sm2.Load(outPath);
            Assert.Equal(new ChamberLayout(90, false), sm2.GetLayout(null));
            Assert.Equal(new ChamberLayout(0, true), sm2.GetLayout("C1"));
            Assert.Equal(30, sm2.GetSource(1)!.AngleDegrees);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(outPath)) File.Delete(outPath);
        }
    }
}
