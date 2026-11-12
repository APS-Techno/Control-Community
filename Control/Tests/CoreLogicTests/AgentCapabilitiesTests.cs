using APS.SystemControl.Core.Remote.Agent;
using APS.SystemControl.Hosting;
using Xunit;

namespace CoreLogicTests;

public class AgentCapabilitiesTests
{
    [Fact]
    public void Community_AdvertisesControlModeOnly()
        => Assert.Equal(new[] { "controlMode" }, AgentCapabilities.For(ControlEdition.Community));
}
