using System.IO;
using System.Xml.Serialization;
using APS.SystemControl.Core;

namespace CoreLogicTests;

public class UserLayoutSymbolScaleTests
{
    private static UserLayout RoundTrip(UserLayout layout)
    {
        var ser = new XmlSerializer(typeof(UserLayout));
        using var sw = new StringWriter();
        ser.Serialize(sw, layout);
        using var sr = new StringReader(sw.ToString());
        return (UserLayout)ser.Deserialize(sr)!;
    }

    [Fact]
    public void SymbolScale_PersistsPerProfileThroughXml()
    {
        var layout = new UserLayout();
        layout.SetSymbolScale("Cluster", 0.82f);
        layout.SetSymbolScale("ADS", 1.1f);
        layout.SetSymbolScale("cluster", 0.75f);

        var loaded = RoundTrip(layout);

        Assert.Equal(0.75f, loaded.GetSymbolScale("Cluster"));
        Assert.Equal(1.1f, loaded.GetSymbolScale("ADS"));
        Assert.Null(loaded.GetSymbolScale("Sputter"));
        Assert.Equal(2, loaded.SymbolScales.Count);
    }

    [Fact]
    public void RemoveSymbolScale_FallsBackToProfileDefault()
    {
        var layout = new UserLayout();
        layout.SetSymbolScale("Cluster", 0.9f);
        layout.RemoveSymbolScale("Cluster");
        Assert.Null(RoundTrip(layout).GetSymbolScale("Cluster"));
    }
}
