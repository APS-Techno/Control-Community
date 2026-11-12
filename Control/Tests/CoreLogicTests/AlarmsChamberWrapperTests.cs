using APS.SystemControl.XML;
using System.Xml.Serialization;

namespace CoreLogicTests;

public class AlarmsChamberWrapperTests
{
    private static XML_Alarms Parse(string xml)
    {
        var ser = new XmlSerializer(typeof(XML_Alarms));
        using var sr = new StringReader(xml);
        return (XML_Alarms)ser.Deserialize(sr)!;
    }

    [Fact]
    public void Flat_config_parses_with_empty_chambers_list()
    {
        var x = Parse(@"<alarms>
  <states initial=""startup""><transition from=""startup"" to=""idle"" sensor=""pm"" cond=""less"" val=""1e-6"" /></states>
  <alarm sensor=""pm"" cond=""more"" val=""1e-4"" state=""idle"">msg</alarm>
</alarms>");
        Assert.NotNull(x.States);
        Assert.Single(x.AlarmList);
        Assert.Empty(x.Chambers);
    }

    [Fact]
    public void Chamber_wrapper_parses_each_chamber_with_states_and_alarms()
    {
        var x = Parse(@"<alarms>
  <chamber id=""Main"">
    <states initial=""startup""><transition from=""startup"" to=""idle"" sensor=""pm"" cond=""less"" val=""1e-6"" /></states>
    <alarm sensor=""pm"" cond=""more"" val=""1e-4"" state=""idle"">msg</alarm>
  </chamber>
</alarms>");
        Assert.Single(x.Chambers);
        Assert.Equal("Main", x.Chambers[0].Id);
        Assert.NotNull(x.Chambers[0].States);
        Assert.Single(x.Chambers[0].AlarmList);
        Assert.Null(x.States);
        Assert.Empty(x.AlarmList);
    }
}
