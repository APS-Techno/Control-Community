using System.Linq;
using System.Reflection;
using APS.SystemControl.Core.Menus;
using CoreLogicTests.Fixtures;
using Xunit;

namespace CoreLogicTests;

public class ContributorDiscoveryTests
{
    [Fact]
    public void DiscoverFromAssemblies_FindsAttributedTypes()
    {
        var contributors = ContributorDiscovery.DiscoverFromAssemblies(
            new[] { typeof(FakeContributor).Assembly });
        Assert.Contains(contributors, c => c.GetType() == typeof(FakeContributor));
    }

    [Fact]
    public void DiscoverFromAssemblies_SkipsTypesWithoutAttribute()
    {
        var contributors = ContributorDiscovery.DiscoverFromAssemblies(
            new[] { typeof(UnattributedContributor).Assembly });
        Assert.DoesNotContain(contributors, c => c.GetType() == typeof(UnattributedContributor));
    }

    [Fact]
    public void DiscoverFromInstances_FiltersByInterface()
    {
        var inputs = new object[] {
            new FakeContributor(),
            "not a contributor",
            new UnattributedContributor(),  // matches by interface even without attribute
        };
        var contributors = ContributorDiscovery.DiscoverFromInstances(inputs).ToList();
        Assert.Equal(2, contributors.Count);
    }
}
