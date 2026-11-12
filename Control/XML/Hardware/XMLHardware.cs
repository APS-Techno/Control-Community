using System.Xml.Linq;
using APS.SystemControl.Core;
using APS.SystemControl.Core.CoreClass;

namespace APS.SystemControl.XML;

public class XMLHWSection(XName name) : XElement(name)
{
    public List<XElement> _list = [];

    public List<XElement> Items => _list;
    public string Lib => this.Attribute("lib")!.Value;

    // short name of the corresponding DLL
    public string HwType => this.Attribute("hwtype")!.Value;

    public string? Host => this.Attribute("host")?.Value;

    public void AddItems(IEnumerable<XElement> items)
    {
        _list.AddRange(items);
    }
}

public class XMLHardware
{
    #region[Fields]

    private readonly XDocument xDocument;
    private readonly HwXmlTypeMapper typeMapper = new();
    public Dictionary<string, List<XMLHWSection>> Sections { get; private set; } = [];

    public XMLHWSection? Libs;

    public XMLHWSection PLCs;

    public XMLHardware(string xmlPath)
    {
        this.xDocument = XDocument.Load(xmlPath);

        Libs = CreateSection("libraries");
        PLCs = CreateSection("plcs");

        InitializeSections();
    }

    private void InitializeSections()
    {
        foreach (KeyValuePair<DeviceType, Type> entry in typeMapper.Type)
        {
            Sections[HwXmlTypeMapper.Name(entry.Key)] = CreateSections(HwXmlTypeMapper.Name(entry.Key));
        }
    }

    private List<XMLHWSection> CreateSections(string name)
    {
        List<XMLHWSection> sectionsList = [];

        var elements = xDocument.Element("hardware")?.Elements(name);
        if (elements != null)
        {
            foreach (var element in elements)
            {
                XMLHWSection section = new (element.Name);
                section.AddItems(element.Elements());
                foreach (var attr in element.Attributes())
                {
                    section.SetAttributeValue(attr.Name, attr.Value);
                }
                sectionsList.Add(section);
            }
        }
        return sectionsList;
    }
    #endregion

    #region[Private definitions]
    private XMLHWSection CreateSection(string name)
    {
        XMLHWSection section = null;

        var libsElement = xDocument.Element("hardware")?.Element(name);
        if (libsElement != null)
        {
            section = new XMLHWSection(libsElement.Name);
            if (section != null)
            {
                section.Value = libsElement.Value;
                foreach (var attr in libsElement.Attributes())
                {
                    section.SetAttributeValue(attr.Name, attr.Value);
                }
                section.AddItems(GetElements(name));
            }
        }
        return section!;
    }

    private List<XElement> GetElements(string elementType)
    {
        var elementsList = new List<XElement>();
        var rootelement = xDocument.Element("hardware")?.Element(elementType);
        if (rootelement != null)
        {
            var elements = rootelement?.Elements();
            if (elements != null)
            {
                foreach (var element in elements)
                {
                    elementsList.Add(element);
                }
            }
        }
        return elementsList;
    }
    #endregion

    #region[Public methods]

    /// <summary>
    /// Metadata attributes that describe routing / physics but are NOT
    /// positional constructor arguments. They must be excluded from the
    /// <see cref="Attributes"/> array so that
    /// <see cref="System.Activator.CreateInstance"/> receives exactly the
    /// constructor parameters it expects.  The helpers that consume these
    /// attributes (e.g. <c>Core.ApplySensorChamber</c>) read them directly
    /// from the original <see cref="XElement"/>, so filtering here is safe.
    /// </summary>
    private static readonly HashSet<string> NonPositionalAttributes = new(StringComparer.OrdinalIgnoreCase)
    {
        "chamber",      // chamber routing — Emulator + Control's ApplySensorChamber
        "valve",        // MFC inlet valves — BaseMFC.GateValves via Core.ApplyMfcGateValves
        "gate",         // pump gate valve — Emulator
        "foreline",     // turbo foreline valve — Emulator
        "connects",     // slit-valve cross-chamber — Emulator forward-looking
        "property",     // sensor type (Pressure/Temperature) — Emulator
        "vent",         // vent flag — Emulator only
        "conductance",  // vent conductance — Emulator
        "delay",        // valve open/close delay — Emulator only
        "speed",        // pump/turbo speed override — Emulator only
        "Kp",           // PID proportional gain — Emulator only
        "Ki",           // PID integral gain — Emulator only
        "Kd",           // PID derivative gain — Emulator only
        "presets",      // stage preset positions — read by BaseStage.ApplyConfig
        "softMin",      // stage lower soft limit — BaseStage.ApplyConfig
        "softMax",      // stage upper soft limit — BaseStage.ApplyConfig
        "jogStep",      // stage default jog increment — BaseStage.ApplyConfig
        "homePos",      // stage home/reference position — BaseStage.ApplyConfig
        "haltByte",     // stage stop-bit V-byte — BaseStage.ApplyConfig
        "haltBit",      // stage stop-bit index — BaseStage.ApplyConfig
                        // (named "halt*" not "stop*" so it can't collide, case-
                        //  insensitively, with the turbo pump's positional "stopbit")
    };

    /// <summary>
    /// Metadata attributes listed in <see cref="NonPositionalAttributes"/> are
    /// excluded from the returned array so they do not reach
    /// <see cref="System.Activator.CreateInstance"/> as spurious constructor arguments.
    /// </summary>
    public static object[] Attributes(XElement element)
    {
        int positionalCount = element.Attributes()
            .Count(a => !NonPositionalAttributes.Contains(a.Name.LocalName));

        int size = positionalCount + 1;
        object[] Attr = new object[size];

        // Trim: an element's text Value is its id (e.g. "SV_LL"). For elements
        // with child elements (the <robot>, with <face>/<timing>), Value also
        // includes the inter-element whitespace, so the id must be trimmed or the
        // device registers under a padded key and recipe device-binding can't
        // find it ("Bound device 'ROBOT' not found"). No-op for normal devices.
        Attr[0] = element.Value.Trim();
        int count = 1;

        foreach (var attribute in element.Attributes())
        {
            if (NonPositionalAttributes.Contains(attribute.Name.LocalName))
                continue;

            // Parse with InvariantCulture: HWConf.xml always uses '.' as the
            // decimal point. On a comma-decimal machine a culture-sensitive
            // parse turns "0.05" into 5 (treating '.' as a group separator),
            // silently feeding a wrong calibration/offset into a driver
            // constructor with no validation.
            if (int.TryParse(attribute.Value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out int result))
            {
                Attr[count] = result;
            }
            else if (double.TryParse(attribute.Value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double result2))
            {
                Attr[count] = result2;
            }
            else
            {
                Attr[count] = attribute.Value;
            }
            count++;
        }
        return Attr;
    }
    #endregion
}
