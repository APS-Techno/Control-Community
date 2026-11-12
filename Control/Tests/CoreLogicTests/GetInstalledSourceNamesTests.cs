using System.IO;
using System.Threading.Channels;
using Xunit;
using ThreadMsg = APS.SystemControl.Core.ThreadMsg;

namespace APS.SystemControl.Tests.CoreLogic;

/// <summary>
/// Core.GetInstalledSourceNames returns effective source names (name attribute
/// else material) rather than raw material names.
/// </summary>
public class GetInstalledSourceNamesTests
{
    [Fact]
    public void Returns_EffectiveNames_FromSourcesXml()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "Sources.xml"),
            "<sources>" +
            "<source flange=\"1\"><material>Si</material></source>" +
            "<source flange=\"2\" name=\"W_1\"><material>W</material></source>" +
            "</sources>");
        try
        {
            var core = new APS.SystemControl.Core.Core(Channel.CreateUnbounded<ThreadMsg>().Writer)
            {
                ConfigDir = dir
            };
            var names = core.GetInstalledSourceNames();
            Assert.Contains("Si", names);
            Assert.Contains("W_1", names);
            Assert.DoesNotContain("W", names); // W was renamed via name attr
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Returns_Empty_WhenConfigDirNotSet()
    {
        var core = new APS.SystemControl.Core.Core(Channel.CreateUnbounded<ThreadMsg>().Writer);
        var names = core.GetInstalledSourceNames();
        Assert.Empty(names);
    }
}
