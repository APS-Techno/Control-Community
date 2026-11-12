using APS.Accounts.Domain;
using APS.Accounts.Services;
using APS.SystemControl.Accounts;
using Xunit;

namespace CoreLogicTests;

public class AuthorizationAdapterTests
{
    private static void SeatPrincipal(params Permission[] permissions)
    {
        var sessions = new SessionManager(new SystemClock());
        sessions.BeginSession(new AuthenticatedPrincipal(
            System.Guid.NewGuid(), "tester", "Tester",
            new[] { "TestRole" }, permissions, isOffline: false, System.Guid.NewGuid()));
        CurrentUser.Sessions = sessions;
    }

    [Fact]
    public void Adapter_grants_only_the_current_users_permissions()
    {
        try
        {
            SeatPrincipal(Permission.RunRecipe);
            var adapter = new AuthorizationAdapter();

            Assert.True(adapter.Can(nameof(Permission.RunRecipe)));
            Assert.False(adapter.Can(nameof(Permission.SafetyOverride)));
        }
        finally { CurrentUser.Sessions = null; }
    }

    [Fact]
    public void Adapter_denies_unknown_permission_names()
    {
        try
        {
            SeatPrincipal(Permission.RunRecipe);
            Assert.False(new AuthorizationAdapter().Can("NotARealPermission"));
        }
        finally { CurrentUser.Sessions = null; }
    }

    [Fact]
    public void Adapter_denies_everything_when_no_one_is_signed_in()
    {
        CurrentUser.Sessions = null;
        Assert.False(new AuthorizationAdapter().Can(nameof(Permission.RunRecipe)));
    }
}
