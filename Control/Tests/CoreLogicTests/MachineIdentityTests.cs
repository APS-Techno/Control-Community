using System.Xml.Linq;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class MachineIdentityTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"aps-ident-{Guid.NewGuid():N}");

    public MachineIdentityTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static XDocument Hw(string body) => XDocument.Parse($"<hardware>{body}</hardware>");

    private const string LoopbackBody = """
        <plcs lib="S7"><plc type="T" address="127.0.0.1">main</plc><plc type="T" address="127.0.0.2">aux</plc></plcs>
        <valves host="main"><valve address="1">V1</valve></valves>
        <powersupplies><powersupply host="localhost" port="5025">P1</powersupply></powersupplies>
        <drives><drive host="::1" port="5032">D1</drive></drives>
        """;

    [Fact]
    public void All_loopback_endpoints_are_an_emulator()
        => Assert.True(MachineIdentity.IsEmulatorProfile(Hw(LoopbackBody), null));

    [Fact]
    public void One_non_loopback_plc_is_physical()
        => Assert.False(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody.Replace("127.0.0.2", "192.168.1.10")), null));

    [Fact]
    public void One_non_loopback_lan_device_is_physical()
        => Assert.False(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody.Replace("localhost", "10.0.0.5")), null));

    [Fact]
    public void Unparseable_hostname_is_physical()
        => Assert.False(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody.Replace("localhost", "plc.lab.example")), null));

    [Fact]
    public void Serial_port_device_is_physical_even_when_every_network_endpoint_is_loopback()
        => Assert.False(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody + """<pumps><pump port="COM3">SP</pump></pumps>"""), null));

    [Fact]
    public void Loopback_tcp_port_uri_is_not_serial()
        => Assert.True(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody + """<pumps><pump port="tcp://127.0.0.1:5040">SP</pump></pumps>"""), null));

    [Fact]
    public void Non_loopback_tcp_port_uri_is_physical()
        => Assert.False(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody + """<pumps><pump port="tcp://10.1.1.1:5040">SP</pump></pumps>"""), null));

    [Fact]
    public void Profile_without_any_endpoint_is_physical()
        => Assert.False(MachineIdentity.IsEmulatorProfile(Hw("<valves><valve>V1</valve></valves>"), null));

    [Fact]
    public void Override_wins_in_both_directions()
    {
        Assert.False(MachineIdentity.IsEmulatorProfile(Hw(LoopbackBody), false));
        Assert.True(MachineIdentity.IsEmulatorProfile(
            Hw(LoopbackBody.Replace("127.0.0.2", "192.168.1.10")), true));
    }

    [Theory]
    [InlineData("emulator", true)]
    [InlineData(" Physical ", false)]
    [InlineData("EMULATOR", true)]
    [InlineData("simulator", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseOverride_accepts_only_the_two_words(string? text, bool? expected)
        => Assert.Equal(expected, MachineIdentity.ParseOverride(text));

    [Fact]
    public void LoadOverride_reads_ExecutionTarget_from_Config_xml()
    {
        File.WriteAllText(Path.Combine(_dir, "Config.xml"),
            "<Config><ExecutionTarget>physical</ExecutionTarget></Config>");
        Assert.False(MachineIdentity.LoadOverride(_dir));
    }

    [Fact]
    public void LoadOverride_is_null_without_element_file_or_directory()
    {
        Assert.Null(MachineIdentity.LoadOverride(_dir));
        File.WriteAllText(Path.Combine(_dir, "Config.xml"), "<Config/>");
        Assert.Null(MachineIdentity.LoadOverride(_dir));
        Assert.Null(MachineIdentity.LoadOverride(null));
    }

    [Fact]
    public void Resolve_combines_hwconf_and_override()
    {
        string hw = Path.Combine(_dir, "HWConf.xml");
        File.WriteAllText(hw, Hw(LoopbackBody).ToString());
        Assert.Equal((true, "emulator"), MachineIdentity.Resolve(_dir, hw));

        File.WriteAllText(Path.Combine(_dir, "Config.xml"),
            "<Config><ExecutionTarget>physical</ExecutionTarget></Config>");
        Assert.Equal((false, "physical"), MachineIdentity.Resolve(_dir, hw));
    }

    [Fact]
    public void Resolve_with_missing_hwconf_is_physical()
        => Assert.Equal((false, "physical"), MachineIdentity.Resolve(_dir, Path.Combine(_dir, "none.xml")));
}
