using System.Text.RegularExpressions;
using System.Xml.Linq;
using APS.SystemControl.Tests.Shared;
using Xunit;

namespace CoreLogicTests;

public class OpenProfileInterlockTests
{
    static readonly HashSet<string> DeviceElements = new(StringComparer.OrdinalIgnoreCase)
        { "valve", "mfc", "sensor", "pump", "stage", "powersupply", "controller", "drive", "robot" };

    static readonly Regex InterlockId = new(@"^(Valve|Power|Pump|Stage|Robot|Drive|Mfc)_(.+)_([A-Za-z0-9]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("Sputter")]
    [InlineData("ADS")]
    [InlineData("Cluster")]
    public void EveryInterlockNamesDevicesOfItsProfile(string profile)
    {
        var devices = XDocument.Load(ConfigsDir.Path($"{profile}/HWConf.xml")).Descendants()
            .Where(e => DeviceElements.Contains(e.Name.LocalName))
            .Select(e => string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unknown = new List<string>();
        foreach (var file in Directory.GetFiles(ConfigsDir.Path(profile), "Interlocks*.xml", SearchOption.AllDirectories))
            foreach (var interlock in XDocument.Load(file).Descendants("interlock"))
            {
                string id = (string?)interlock.Attribute("id") ?? "";
                var m = InterlockId.Match(id);
                if (m.Success && !devices.Contains(m.Groups[2].Value))
                    unknown.Add($"{Path.GetFileName(file)}: {id}");
                foreach (var line in interlock.Elements("l"))
                {
                    var parts = line.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && !devices.Contains(parts[1]))
                        unknown.Add($"{Path.GetFileName(file)}: {id}: {line.Value}");
                }
            }

        Assert.Empty(unknown);
    }
}
