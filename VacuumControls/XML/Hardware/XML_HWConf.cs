using System.Xml.Linq;

namespace APS.Controls.XML.Hardware
{
    public class HWLibrary
    {
        public string Lib { get; set; } = string.Empty;
        public string Dll { get; set; } = string.Empty;
    }

    public class HWPlc
    {
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
    }

    public class HWElement
    {
        public string Id { get; set; }
        public string Description { get; set; }
        public string SectionName { get; set; }
        public string HwType { get; set; }
        public string? Host { get; set; }

        /// <summary>All XML attributes on the child element except "descr".</summary>
        public Dictionary<string, string> Attributes { get; set; } = new();

        public HWElement(string id, string description, string sectionName, string hwType, string? host)
        {
            Id = id;
            Description = description;
            SectionName = sectionName;
            HwType = hwType;
            Host = host;
        }
    }

    /// <summary>
    /// Holds a reference to the underlying XElement for round-trip save fidelity.
    /// </summary>
    public class HWSection
    {
        public string SectionName { get; }
        public string Lib { get; }
        public string HwType { get; }
        public string? Host { get; internal set; }
        public List<HWElement> Elements { get; } = new();

        internal XElement XmlNode { get; }

        public HWSection(XElement element)
        {
            XmlNode = element;
            SectionName = element.Name.LocalName;
            Lib = (string?)element.Attribute("lib") ?? string.Empty;
            HwType = (string?)element.Attribute("hwtype") ?? string.Empty;
            Host = (string?)element.Attribute("host");

            foreach (var child in element.Elements())
            {
                var id = child.Value.Trim();
                var descr = (string?)child.Attribute("descr") ?? string.Empty;

                var attrs = new Dictionary<string, string>();
                foreach (var attr in child.Attributes())
                {
                    if (attr.Name.LocalName != "descr")
                        attrs[attr.Name.LocalName] = attr.Value;
                }

                var hwElement = new HWElement(id, descr, SectionName, HwType, Host)
                {
                    Attributes = attrs
                };
                Elements.Add(hwElement);
            }
        }

        /// <summary>
        /// The caller is responsible for attaching the new XElement to the document root.
        /// </summary>
        internal HWSection(string sectionName, string lib, string hwType, string? host)
        {
            XmlNode = new XElement(sectionName);
            SectionName = sectionName;
            Lib = lib;
            HwType = hwType;
            Host = host;

            if (!string.IsNullOrEmpty(lib))   XmlNode.SetAttributeValue("lib", lib);
            if (!string.IsNullOrEmpty(hwType)) XmlNode.SetAttributeValue("hwtype", hwType);
            if (!string.IsNullOrEmpty(host))   XmlNode.SetAttributeValue("host", host);
        }

        /// <summary>
        /// The child tag name is the singular of the section name
        /// (e.g. "valves" → "valve", "powersupplies" → "powersupply").
        /// </summary>
        public void AddElement(HWElement element)
        {
            Elements.Add(element);

            var tagName = DeriveSingular(SectionName);
            var child = new XElement(tagName);
            child.Value = element.Id;
            child.SetAttributeValue("descr", element.Description);
            foreach (var kvp in element.Attributes)
                child.SetAttributeValue(kvp.Key, kvp.Value);

            XmlNode.Add(child);
        }

        public void RemoveElement(string id)
        {
            var element = Elements.FirstOrDefault(e => e.Id == id);
            if (element == null) return;

            Elements.Remove(element);

            var child = XmlNode.Elements()
                .FirstOrDefault(x => x.Value.Trim() == id);
            child?.Remove();
        }

        /// <summary>
        /// Updates a field on an element and the corresponding XElement attribute (or
        /// child text Value for "ID"). fieldName is "ID", "Description", or any
        /// attribute key (e.g. "address", "index", "port"). Keeps the C# object and
        /// the XDocument in lock-step so HWConfModel.Save() round-trips edits.
        /// </summary>
        public void UpdateElement(HWElement element, string fieldName, string value)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (string.IsNullOrEmpty(fieldName)) throw new ArgumentException("Field name is required.", nameof(fieldName));
            if (!Elements.Contains(element))
                throw new InvalidOperationException($"Element '{element.Id}' does not belong to this section.");

            // Locate the underlying XElement child. We match by current Id (matches
            // the existing RemoveElement convention) — so update Id LAST in the
            // in-memory object, but find the XElement BEFORE we change anything.
            var xChild = XmlNode.Elements()
                .FirstOrDefault(x => x.Value.Trim() == element.Id);
            if (xChild == null)
                throw new InvalidOperationException(
                    $"Element '{element.Id}' was not found in section's XML — in-memory model and XDocument have diverged.");

            switch (fieldName)
            {
                case "ID":
                    xChild.Value = value;
                    element.Id = value;
                    break;
                case "Description":
                    xChild.SetAttributeValue("descr", value);
                    element.Description = value;
                    break;
                default:
                    xChild.SetAttributeValue(fieldName, value);
                    element.Attributes[fieldName] = value;
                    break;
            }
        }

        private static string DeriveSingular(string sectionName)
        {
            if (sectionName == "powersupplies") return "powersupply";
            if (sectionName == "libraries") return "library";
            if (sectionName.EndsWith('s')) return sectionName[..^1];
            return sectionName;
        }
    }

    /// <summary>
    /// Root model for HWConf.xml. Parsed using LINQ to XML for round-trip fidelity
    /// and support for varying attribute structures per section.
    /// </summary>
    public class HWConfModel
    {
        private static readonly string[] _sectionNames =
        [
            "valves", "mfcs", "sensors", "pumps", "stages",
            "controllers", "powersupplies", "drives", "robots"
        ];

        private XDocument _doc = new();
        private XElement? _rootEl;
        private XElement? _plcsEl;

        public List<HWLibrary> Libraries { get; private set; } = new();
        public List<HWPlc> PLCs { get; private set; } = new();

        private readonly List<HWSection> _sections = new();

        private HWConfModel() { }

        public static HWConfModel Load(string path)
        {
            var model = new HWConfModel();
            model._doc = XDocument.Load(path);
            model.Parse();
            return model;
        }

        private void Parse()
        {
            _rootEl = _doc.Root;
            if (_rootEl == null) return;
            var root = _rootEl;

            var librariesEl = root.Element("libraries");
            if (librariesEl != null)
            {
                foreach (var lib in librariesEl.Elements("library"))
                {
                    Libraries.Add(new HWLibrary
                    {
                        Lib = (string?)lib.Attribute("lib") ?? string.Empty,
                        Dll = lib.Value.Trim()
                    });
                }
            }

            _plcsEl = root.Element("plcs");
            if (_plcsEl != null)
            {
                foreach (var plc in _plcsEl.Elements("plc"))
                {
                    PLCs.Add(new HWPlc
                    {
                        Name = plc.Value.Trim(),
                        Type = (string?)plc.Attribute("type") ?? string.Empty,
                        Address = (string?)plc.Attribute("address") ?? string.Empty
                    });
                }
            }

            foreach (var el in root.Elements())
            {
                if (_sectionNames.Contains(el.Name.LocalName))
                {
                    _sections.Add(new HWSection(el));
                }
            }
        }

        public void Save(string path)
        {
            _doc.Save(path);
        }

        /// <summary>There may be multiple sections with the same name, e.g. &lt;sensors&gt;.</summary>
        public List<HWSection> GetSections(string name)
            => _sections.Where(s => s.SectionName == name).ToList();

        public List<string> GetSectionNames()
            => _sections.Select(s => s.SectionName).Distinct().ToList();

        public List<HWElement> GetAllElements()
            => _sections.SelectMany(s => s.Elements).ToList();

        /// <summary>
        /// If &lt;plcs&gt; does not yet exist, it is created right after &lt;libraries&gt;
        /// (or as the first child of the root).
        /// </summary>
        public void AddPlc(HWPlc plc)
        {
            if (plc == null) throw new ArgumentNullException(nameof(plc));
            if (string.IsNullOrWhiteSpace(plc.Name))
                throw new ArgumentException("PLC name is required.", nameof(plc));
            plc.Name = plc.Name.Trim();
            if (PLCs.Any(p => p.Name == plc.Name))
                throw new InvalidOperationException($"PLC '{plc.Name}' already exists.");

            if (_plcsEl == null)
            {
                if (_rootEl == null)
                    throw new InvalidOperationException("Document has no root element.");
                _plcsEl = new XElement("plcs");
                var librariesEl = _rootEl.Element("libraries");
                if (librariesEl != null)
                    librariesEl.AddAfterSelf(_plcsEl);
                else
                    _rootEl.AddFirst(_plcsEl);
            }

            PLCs.Add(plc);

            var child = new XElement("plc")
            {
                Value = plc.Name
            };
            child.SetAttributeValue("type", plc.Type);
            child.SetAttributeValue("address", plc.Address);
            _plcsEl.Add(child);
        }

        /// <summary>
        /// Does NOT touch sections that reference the removed PLC via host= — the caller
        /// is responsible for warning the user or reassigning those sections
        /// (use <see cref="GetSectionsReferencing"/>).
        /// </summary>
        public void RemovePlc(string name)
        {
            var plc = PLCs.FirstOrDefault(p => p.Name == name);
            if (plc == null) return;
            PLCs.Remove(plc);

            if (_plcsEl == null) return;
            var child = _plcsEl.Elements("plc")
                .FirstOrDefault(x => x.Value.Trim() == name);
            child?.Remove();
        }

        /// <summary>
        /// Renames a PLC and cascades the change to every section's host= attribute
        /// and every HWElement.Host field that referenced the old name.
        /// </summary>
        public void RenamePlc(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("New name is required.", nameof(newName));
            newName = newName.Trim();
            if (oldName == newName) return;
            if (PLCs.Any(p => p.Name == newName))
                throw new InvalidOperationException($"PLC '{newName}' already exists.");

            var plc = PLCs.FirstOrDefault(p => p.Name == oldName);
            if (plc == null) return;
            plc.Name = newName;

            if (_plcsEl != null)
            {
                var child = _plcsEl.Elements("plc")
                    .FirstOrDefault(x => x.Value.Trim() == oldName);
                if (child != null) child.Value = newName;
            }

            foreach (var section in _sections.Where(s => s.Host == oldName))
            {
                section.Host = newName;
                section.XmlNode.SetAttributeValue("host", newName);
                foreach (var el in section.Elements)
                    el.Host = newName;
            }
        }

        public List<HWSection> GetSectionsReferencing(string plcName)
            => _sections.Where(s => s.Host == plcName).ToList();

        public XElement? GetRootElement() => _rootEl;

        /// <summary>
        /// Appended at the end of the document.
        /// </summary>
        public HWSection AddSection(string sectionName, string lib, string hwType, string? host)
        {
            if (_rootEl == null)
                throw new InvalidOperationException("Document has no root element.");
            if (!_sectionNames.Contains(sectionName))
                throw new ArgumentException($"Unknown section name '{sectionName}'.", nameof(sectionName));

            var section = new HWSection(sectionName, lib, hwType, host);
            _rootEl.Add(section.XmlNode);
            _sections.Add(section);
            return section;
        }

        /// <summary>
        /// Throws if the section still has elements — the caller must clear them first.
        /// </summary>
        public void RemoveSection(HWSection section)
        {
            if (section == null) throw new ArgumentNullException(nameof(section));
            if (section.Elements.Count > 0)
                throw new InvalidOperationException(
                    $"Section <{section.SectionName} hwtype=\"{section.HwType}\" host=\"{section.Host}\"> still contains {section.Elements.Count} element(s). Remove them first.");
            if (!_sections.Remove(section))
                throw new InvalidOperationException("Section does not belong to this model.");
            section.XmlNode.Remove();
        }

        /// <summary>
        /// Also moves the underlying XElement, then updates HWElement.Host /
        /// HWElement.HwType to match the target section.
        /// </summary>
        public void MoveElement(HWElement element, HWSection target)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (target == null) throw new ArgumentNullException(nameof(target));

            var source = _sections.FirstOrDefault(s => s.Elements.Contains(element))
                ?? throw new InvalidOperationException("Element does not belong to any section in this model.");
            if (source == target) return;
            if (source.SectionName != target.SectionName)
                throw new InvalidOperationException(
                    $"Cannot move element between different section types ({source.SectionName} -> {target.SectionName}).");

            var xChild = source.XmlNode.Elements()
                .FirstOrDefault(x => x.Value.Trim() == element.Id);
            if (xChild == null)
                throw new InvalidOperationException(
                    $"Element '{element.Id}' was not found in source section's XML — in-memory model and XDocument have diverged.");

            xChild.Remove();
            target.XmlNode.Add(xChild);

            source.Elements.Remove(element);
            target.Elements.Add(element);

            element.Host = target.Host;
            element.HwType = target.HwType;
        }
    }
}
