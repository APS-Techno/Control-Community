using System.Xml.Linq;
using EmulatorCore.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using EmulatorCore.Models;
using APS.Emulator.Devices.S7;
using static EmulatorCore.Engine.HwConfAttributes;

namespace APS.SystemEmulator.Engine;

public class ConfigLoader(DeviceRegistry registry, PhysicsConfig physics)
{
    private readonly PhysicsConfig _physics = physics;

    public List<IDeviceEmulator> LoadHWConfig(string hwConfPath)
    {
        var devices = new List<IDeviceEmulator>();
        var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var doc = XDocument.Load(hwConfPath);
        var hardware = doc.Root ?? throw new InvalidOperationException("Invalid HWConf.xml: no root element");

        foreach (var section in hardware.Elements())
        {
            string sectionName = section.Name.LocalName;
            string hwtype = section.Attribute("hwtype")?.Value ?? "";
            string host = section.Attribute("host")?.Value ?? "";

            if (registry.TryGetSectionLoader(sectionName, out var loadSection))
            {
                devices.AddRange(loadSection(section, host));
                continue;
            }

            if (hwtype.Length == 0)
                continue;

            string shortType = ExtractShortType(hwtype);

            foreach (var element in section.Elements())
            {
                var ctx = new DeviceCreateContext(element, sectionName, shortType, host, _physics);
                if (!registry.TryCreate(ctx, out var device))
                {
                    skipped[shortType] = skipped.GetValueOrDefault(shortType) + 1;
                    continue;
                }
                if (device != null)
                    devices.Add(device);
            }

        }

        foreach (var (type, count) in skipped)
            EmulatorLog.Log(LogLevel.Warning, "ConfigLoader",
                $"No device model registered for '{type}'; {count} device(s) skipped");

        // Substrate rotation is hardcoded in the Delphi PLC class (not in HWConf):
        // setpoint DB1:1974, feedback DB1:1978, enable MB30 bit 0. Host comes from
        // HWConf's first <plc> entry so multi-PLC configs route ROT to the correct
        // S7 server (or fall through the "both empty" legacy path for old configs
        // with no PlcId set on their server).
        string rotHost = hardware.Element("plcs")?.Elements("plc").FirstOrDefault()?.Value.Trim() ?? "";
        devices.Add(new RotationEmulator("ROT", 1974, 1978, 30, 0, "Substrate rotation", rotHost));

        // An MFC whose valve= attribute names a non-existent valve never flows
        // through it (unknown valve reads as closed) — surface the typo instead
        // of failing silent. valve= may be a comma-separated manifold list.
        var valveIds = devices.Where(d => d.Category == DeviceCategory.Valve)
                              .Select(d => d.Id).ToHashSet();
        foreach (var mfc in devices.OfType<MFCEmulator>())
            foreach (var v in mfc.InletValveId.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!valveIds.Contains(v))
                    EmulatorLog.Log(LogLevel.Warning, "ConfigLoader",
                        $"MFC '{mfc.Id}' references inlet valve '{v}' which is not defined in {Path.GetFileName(hwConfPath)} — that inlet always reads closed.");

        return devices;
    }

    private static string ExtractShortType(string hwtype)
    {
        // "Vendor.S7200GasValve" -> "S7200GasValve"
        int dotIndex = hwtype.LastIndexOf('.');
        return dotIndex >= 0 ? hwtype[(dotIndex + 1)..] : hwtype;
    }

    public static List<VentMapping> LoadVentMappings(string hwConfPath)
    {
        var mappings = new List<VentMapping>();
        var doc = XDocument.Load(hwConfPath);
        var hardware = doc.Root;
        if (hardware == null) return mappings;

        foreach (var section in hardware.Elements())
        {
            foreach (var element in section.Elements())
            {
                if (element.Attribute("vent")?.Value == "true")
                {
                    string valveId = element.Value.Trim();
                    string chamber = element.Attribute("chamber")?.Value ?? "Main";
                    float conductance = ParseFloat(element, "conductance");
                    if (conductance <= 0) conductance = 2.0f;
                    mappings.Add(new VentMapping
                    {
                        ValveId = valveId,
                        Chamber = chamber,
                        Conductance = conductance,
                    });
                }
            }
        }
        return mappings;
    }

    public static List<SlitConnection> LoadSlitConnections(string hwConfPath)
    {
        var list = new List<SlitConnection>();
        var doc = XDocument.Load(hwConfPath);
        var hardware = doc.Root;
        if (hardware == null) return list;

        foreach (var section in hardware.Elements())
        {
            foreach (var element in section.Elements())
            {
                string? connects = element.Attribute("connects")?.Value;
                if (string.IsNullOrEmpty(connects)) continue;
                string[] parts = connects.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2) continue;
                list.Add(new SlitConnection
                {
                    ValveId = element.Value.Trim(),
                    ChamberA = parts[0],
                    ChamberB = parts[1],
                });
            }
        }
        return list;
    }

    /// <summary>
    /// Validates that every chamber declared in EmulatorConfig is referenced by
    /// at least one sensor and one pump in HWConf, and that every chamber= attribute
    /// on a device resolves to a declared chamber. Throws InvalidOperationException
    /// on any violation, listing all errors at once for the operator to read.
    /// </summary>
    public static void Validate(IReadOnlyList<ChamberConfig> declaredChambers, string hwConfPath)
    {
        var declared = declaredChambers.Select(c => c.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (declared.Count == 0)
            throw new InvalidOperationException(
                $"EmulatorConfig.xml has no <Chamber> entries — engine cannot boot.");

        var errors = new List<string>();
        var doc = XDocument.Load(hwConfPath);
        var hardware = doc.Root ?? throw new InvalidOperationException("Invalid HWConf.xml");

        var sensorsByChamber = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var pumpsByChamber   = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in hardware.Elements())
        {
            string sectionName = section.Name.LocalName;
            foreach (var element in section.Elements())
            {
                string? chamber = element.Attribute("chamber")?.Value;
                if (string.IsNullOrEmpty(chamber)) continue;

                if (!declared.Contains(chamber))
                    errors.Add($"{element.Value.Trim()}: chamber=\"{chamber}\" is not declared in EmulatorConfig.xml.");

                if (sectionName == "sensors")
                {
                    sensorsByChamber.TryAdd(chamber, 0);
                    sensorsByChamber[chamber]++;
                }
                else if (sectionName == "pumps")
                {
                    pumpsByChamber.TryAdd(chamber, 0);
                    pumpsByChamber[chamber]++;
                }
            }
        }

        foreach (var id in declared)
        {
            if (!sensorsByChamber.ContainsKey(id))
                errors.Add($"Chamber \"{id}\" has no <sensor> with chamber=\"{id}\" in HWConf.xml.");
            if (!pumpsByChamber.ContainsKey(id))
                errors.Add($"Chamber \"{id}\" has no <pump> with chamber=\"{id}\" in HWConf.xml.");
        }

        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Configuration validation failed for {hwConfPath}:\n  - " + string.Join("\n  - ", errors));
    }


}
