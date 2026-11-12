using System.IO;
using System.Linq;
using APS.SystemControl.Core.Menus;
using APS.SystemControl.Plugin;
using Xunit;

namespace CoreLogicTests;

public class XmlMenuLoaderTests
{
    private static string WriteTemp(string xml)
    {
        string path = Path.Combine(Path.GetTempPath(), $"menus-{System.Guid.NewGuid():N}.xml");
        File.WriteAllText(path, xml);
        return path;
    }

    [Fact]
    public void Empty_Menus_Returns_NoContributors()
    {
        string path = WriteTemp("<menus />");
        try
        {
            var contributors = XmlMenuLoader.Load(path);
            Assert.Empty(contributors);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Single_Contribution_ProducesOneContributorOneItem()
    {
        string path = WriteTemp(
            """
            <menus>
              <contribution path="Tools/Demo" order="5" action="command:Drive:Holder:Stop" />
            </menus>
            """);
        try
        {
            var contributors = XmlMenuLoader.Load(path);
            var c = Assert.Single(contributors);
            var item = Assert.Single(c.GetMenuItems(StubContext.Instance));
            Assert.Equal("Tools/Demo", item.Path);
            Assert.Equal(5, item.Order);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Missing_File_Returns_NoContributors()
    {
        string path = Path.Combine(Path.GetTempPath(), "does-not-exist.xml");
        var contributors = XmlMenuLoader.Load(path);
        Assert.Empty(contributors);
    }
}

internal sealed class StubContext : IMenuContext
{
    public static readonly StubContext Instance = new();
    public IHwCommandSink Hw => null!;
    public IHardwareRegistry Hardware => null!;
    public IUiServices Ui => null!;
    public IFlagService Flags => null!;
    public IRuntimeState Runtime => null!;
    public IAuthorization Auth => PermissiveAuthorization.Instance;
    public Form Owner => null!;
}
