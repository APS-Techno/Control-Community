using System.Globalization;
using APS.SystemControl.XML;
using APS.SystemControl.XML.Core;

namespace APS.SystemControl.Core.Classes;

public class SourceManager
{
    private readonly Dictionary<int, Source> _sources = new();
    public int FlangeCount { get; }

    // Per-chamber ring layout. "" key not used; lookups fall back to _defaultLayout.
    private readonly Dictionary<string, ChamberLayout> _layouts = new(StringComparer.Ordinal);
    private ChamberLayout _defaultLayout = new(0.0, true);

    public ChamberLayout GetLayout(string? chamber) =>
        !string.IsNullOrEmpty(chamber) && _layouts.TryGetValue(chamber, out var cl) ? cl : _defaultLayout;

    public List<MagnetronDef> Magnetrons { get; set; } = [];
    public List<string> PowerSources { get; set; } = [];
    public List<SourceFormat> Formats { get; set; } = [];
    public List<MagnetronInstance> MagnetronInstances { get; set; } = [];

    // Power-supply id -> ELN (lab-notebook) page. Parallel to PowerSources so existing
    // consumers keep seeing a plain id list; the snapshot reads links via GetPowerSourceElnLink.
    private Dictionary<string, string?> _powerSourceElnById = new(StringComparer.Ordinal);

    /// <summary>ELN link configured for a power supply in Sources.xml, or null when none/unknown.</summary>
    public string? GetPowerSourceElnLink(string powerSupplyId) =>
        !string.IsNullOrEmpty(powerSupplyId) && _powerSourceElnById.TryGetValue(powerSupplyId, out var v) ? v : null;

    public SourceManager(int flangeCount = 6)
    {
        FlangeCount = flangeCount;
        InitializeSources();
    }

    private void InitializeSources()
    {
        for (int i = 1; i <= FlangeCount; i++)
            _sources[i] = new Source { FlangeNumber = i };
    }

    public Source? GetSource(int flangeNumber) => _sources.GetValueOrDefault(flangeNumber);
    public IReadOnlyDictionary<int, Source> Sources => _sources;

    /// <summary>
    /// Maps each power-supply id to the magnetron-instance label it drives, for lighting
    /// the matching schematic source widget (widget id == instance label). A supply that
    /// drives more than one source is ambiguous and excluded; sources missing a power
    /// supply or magnetron instance (or whose instance has no label) are skipped.
    /// </summary>
    public IReadOnlyDictionary<string, string> BuildPsuToMagnetronLabel()
    {
        var labelById = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var m in MagnetronInstances)
            if (!string.IsNullOrEmpty(m.Id) && !string.IsNullOrEmpty(m.Label))
                labelById[m.Id] = m.Label;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);

        foreach (var s in _sources.Values)
        {
            if (string.IsNullOrEmpty(s.PowerSupplyId) || string.IsNullOrEmpty(s.MagnetronInstanceId))
                continue;
            if (!labelById.TryGetValue(s.MagnetronInstanceId, out var label))
                continue;
            if (ambiguous.Contains(s.PowerSupplyId))
                continue;
            if (!result.TryAdd(s.PowerSupplyId, label))
            {
                // Same PSU already mapped to a different source -> can't pick one widget.
                result.Remove(s.PowerSupplyId);
                ambiguous.Add(s.PowerSupplyId);
            }
        }
        return result;
    }

    public void StartDeposition(int flangeNumber) => GetSource(flangeNumber)?.StartDeposition();
    public void StopDeposition(int flangeNumber) => GetSource(flangeNumber)?.StopDeposition();
    public void StopAllDepositions()
    {
        foreach (var s in _sources.Values)
            s.StopDeposition();
    }

    public void Load(string filePath)
    {
        var xmlSources = Serializer<XML_Sources>.Load(filePath);
        if (xmlSources == null) return;

        static bool ParseCw(string? dir) => !string.Equals(dir, "ccw", StringComparison.OrdinalIgnoreCase);
        static double ParseDeg(string? s, double fallback) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

        if (xmlSources.Magnetrons != null)
            Magnetrons = xmlSources.Magnetrons
                .Select(m => new MagnetronDef(m.Id, m.DisplayName))
                .ToList();

        if (xmlSources.PowerSources != null)
        {
            var live = xmlSources.PowerSources
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .ToList();
            PowerSources = live.Select(p => p.Id).ToList();
            _powerSourceElnById = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var p in live)
                _powerSourceElnById[p.Id] = string.IsNullOrWhiteSpace(p.ElnLink) ? null : p.ElnLink;
        }

        if (xmlSources.Formats != null)
            Formats = xmlSources.Formats
                .Select(f => new SourceFormat(f.Id, f.Label))
                .ToList();

        if (xmlSources.MagnetronInstances != null)
            MagnetronInstances = xmlSources.MagnetronInstances
                .Select(m => new MagnetronInstance(m.Id, m.Label, m.Serial, m.ModelId,
                    m.Format, m.PhysicsJson, m.Comment, string.IsNullOrEmpty(m.State) ? "Active" : m.State,
                    m.ElnLink))
                .ToList();

        if (xmlSources.Sources != null)
        {
            foreach (var xs in xmlSources.Sources)
            {
                if (_sources.TryGetValue(xs.FlangeNumber, out Source? source))
                {
                    source.Chamber = string.IsNullOrEmpty(xs.Chamber) ? null : xs.Chamber;
                    source.ShutterId = string.IsNullOrEmpty(xs.ShutterId) ? null : xs.ShutterId;
                    source.Name = string.IsNullOrEmpty(xs.Name) ? "" : xs.Name;
                    source.Material = xs.Material ?? "";
                    source.MagnetronId = xs.MagnetronId ?? "";
                    source.MagnetronInstanceId = xs.MagnetronInstanceId ?? "";
                    source.PowerSupplyId = xs.PowerSupplyId ?? "";
                    source.Model = xs.Model ?? "";
                    source.PowerType = xs.PowerType ?? "";
                    source.Description = xs.Description ?? "";
                    source.Comment = xs.Comment ?? "";
                    // Parse with InvariantCulture so Sources.xml round-trips
                    // regardless of the machine locale / calendar (audit U-7).
                    if (DateTime.TryParse(xs.InstalledDate, CultureInfo.InvariantCulture,
                            DateTimeStyles.None, out DateTime dt))
                        source.InstalledDate = dt;
                    if (TimeSpan.TryParse(xs.TotalDepositionTime, CultureInfo.InvariantCulture,
                            out TimeSpan ts))
                        source.TotalDepositionTime = ts;
                    source.AngleDegrees = double.TryParse(xs.Angle, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var ang) ? ang : null;
                    source.ZeroDistanceMm = double.TryParse(xs.ZeroDistanceMm, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var zd) ? zd : null;
                    source.LiftPositionMm = double.TryParse(xs.LiftMm, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var lp) ? lp : null;
                }
            }
        }

        _layouts.Clear();
        _defaultLayout = new ChamberLayout(0.0, true);
        if (xmlSources.Layout is { } lay)
        {
            _defaultLayout = new ChamberLayout(ParseDeg(lay.Start, 0.0), ParseCw(lay.Direction));
            if (lay.Chambers != null)
                foreach (var c in lay.Chambers)
                    if (!string.IsNullOrEmpty(c.Id))
                        _layouts[c.Id] = new ChamberLayout(
                            ParseDeg(c.Start, _defaultLayout.StartDegrees),
                            c.Direction is null ? _defaultLayout.Clockwise : ParseCw(c.Direction));
        }
    }

    public void Save(string filePath)
    {
        var xmlSources = new XML_Sources
        {
            Magnetrons = Magnetrons.Select(m => new XML_Magnetron
            {
                Id = m.Id,
                DisplayName = m.DisplayName
            }).ToList(),

            PowerSources = PowerSources.Select(id => new XML_PowerSource
            {
                IdAttr = id,
                ElnLink = _powerSourceElnById.GetValueOrDefault(id),
            }).ToList(),

            Formats = Formats.Select(f => new XML_Format { Id = f.Id, Label = f.Label }).ToList(),

            MagnetronInstances = MagnetronInstances.Select(m => new XML_MagnetronInstance
            {
                Id = m.Id, Label = m.Label, Serial = m.Serial, ModelId = m.ModelId,
                Format = m.Format, PhysicsJson = m.PhysicsJson, Comment = m.Comment, State = m.State,
                ElnLink = m.ElnLink,
            }).ToList(),

            Layout = BuildLayoutXml(),

            Sources = _sources.Values.Select(s => new XML_Source
            {
                FlangeNumber = s.FlangeNumber,
                Chamber = s.Chamber,
                ShutterId = s.ShutterId,
                Name = string.IsNullOrEmpty(s.Name) ? null : s.Name,
                Angle = s.AngleDegrees?.ToString(CultureInfo.InvariantCulture),
                ZeroDistanceMm = s.ZeroDistanceMm?.ToString(CultureInfo.InvariantCulture),
                LiftMm = s.LiftPositionMm?.ToString(CultureInfo.InvariantCulture),
                Material = s.Material,
                MagnetronId = s.MagnetronId,
                MagnetronInstanceId = s.MagnetronInstanceId,
                PowerSupplyId = s.PowerSupplyId,
                Model = s.Model,
                PowerType = s.PowerType,
                Description = s.Description,
                Comment = s.Comment,
                InstalledDate = s.InstalledDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                TotalDepositionTime = s.TotalDepositionTime.ToString("c", CultureInfo.InvariantCulture),
            }).ToList()
        };
        Serializer<XML_Sources>.Save(xmlSources, filePath);
    }

    // Emit <layout> only when it carries information (non-default global, or any per-chamber
    // entry) so single-chamber default configs round-trip without gaining an empty element.
    private XML_Layout? BuildLayoutXml()
    {
        bool hasChambers = _layouts.Count > 0;
        bool nonDefault = _defaultLayout.StartDegrees != 0.0 || !_defaultLayout.Clockwise;
        if (!hasChambers && !nonDefault) return null;
        return new XML_Layout
        {
            Start = _defaultLayout.StartDegrees.ToString(CultureInfo.InvariantCulture),
            Direction = _defaultLayout.Clockwise ? "cw" : "ccw",
            Chambers = hasChambers
                ? _layouts.Select(kv => new XML_LayoutChamber
                  {
                      Id = kv.Key,
                      Start = kv.Value.StartDegrees.ToString(CultureInfo.InvariantCulture),
                      Direction = kv.Value.Clockwise ? "cw" : "ccw",
                  }).ToList()
                : null,
        };
    }
}

public sealed record ChamberLayout(double StartDegrees, bool Clockwise);
