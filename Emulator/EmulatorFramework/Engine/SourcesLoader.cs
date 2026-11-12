using System.Xml.Linq;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;

namespace APS.SystemEmulator.Engine;

/// <summary>
/// Loads magnetron-source definitions from Sources.xml and resolves them against
/// the emulator's loaded device list and magnetron config.
/// </summary>
public static class SourcesLoader
{
    public static List<SourceEntry> Load(
        string sourcesXmlPath,
        IReadOnlyList<IDeviceEmulator> devices,
        IReadOnlyList<MagnetronConfig> magnetrons)
    {
        var result = new List<SourceEntry>();

        if (!File.Exists(sourcesXmlPath))
        {
            EmulatorLog.Log(LogLevel.Warning, "Sources",
                $"Sources.xml not found at '{sourcesXmlPath}' — no magnetron sources loaded");
            return result;
        }

        XDocument doc;
        try
        {
            doc = XDocument.Load(sourcesXmlPath);
        }
        catch (Exception ex)
        {
            EmulatorLog.Log(LogLevel.Warning, "Sources", $"Failed to parse {sourcesXmlPath}: {ex.Message}");
            return result;
        }

        var root = doc.Root;
        if (root == null) return result;

        var deviceById = devices.ToDictionary(d => d.Id, d => d);
        var magnetronById = magnetrons.ToDictionary(m => m.Id, m => m);

        foreach (var src in root.Elements("source"))
        {
            string magId = src.Element("magnetronId")?.Value.Trim() ?? "";
            string psuId = src.Element("powerSupplyId")?.Value.Trim() ?? "";

            if (string.IsNullOrEmpty(magId) || string.IsNullOrEmpty(psuId))
                continue; // empty slot

            int flange = int.TryParse(src.Attribute("flange")?.Value, out int f) ? f : 0;

            if (!deviceById.TryGetValue(psuId, out var device))
            {
                EmulatorLog.Log(LogLevel.Warning, "Sources",
                    $"Flange {flange}: power supply '{psuId}' not found — source skipped");
                continue;
            }

            if (device is not IPowerSupplyEmulator psu)
            {
                EmulatorLog.Log(LogLevel.Warning, "Sources",
                    $"Flange {flange}: device '{psuId}' does not implement IPowerSupplyEmulator — source skipped");
                continue;
            }

            if (!magnetronById.TryGetValue(magId, out var mag))
            {
                EmulatorLog.Log(LogLevel.Warning, "Sources",
                    $"Flange {flange}: magnetron '{magId}' not configured in <Magnetrons> — source skipped");
                continue;
            }

            result.Add(new SourceEntry(flange, magId, mag.Voltage, mag.ChamberId, psu));
            EmulatorLog.Log(LogLevel.Info, "Sources",
                $"Flange {flange}: {magId} (V={mag.Voltage}) driven by {psuId} [{psu.RegulationMode}]");
        }

        return result;
    }
}
