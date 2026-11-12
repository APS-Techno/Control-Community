using APS.Accounts.Domain;
using APS.SystemControl.Hosting;

namespace CoreLogicTests;

public class ControlEditionTests
{
    [Fact]
    public void Community_AuthorizeProduct_ReturnsTrueWithoutLicenseFile()
        => Assert.True(ControlEdition.Community.Startup.AuthorizeProduct());

    [Fact]
    public void Community_OpenSession_SeatsLocalAdminWithEveryPermission()
    {
        var sessions = ControlEdition.Community.Startup.OpenSession();
        Assert.NotNull(sessions);
        Assert.All(Enum.GetValues<Permission>(), p => Assert.True(sessions!.Current!.Can(p)));
    }

    [Fact]
    public void Community_HasNoModules()
        => Assert.Empty(ControlEdition.Community.Modules);
}
