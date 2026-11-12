using System.Xml.Linq;
using EmulatorCore.Interfaces;

namespace EmulatorCore.Engine;

public sealed class DeviceRegistry : IDeviceRegistry
{
    private readonly Dictionary<string, Func<DeviceCreateContext, IDeviceEmulator?>> _factories = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<XElement, string, IEnumerable<IDeviceEmulator>>> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IDeviceRegistration> _registrations;

    public DeviceRegistry(IEnumerable<IDeviceRegistration> registrations)
    {
        _registrations = [.. registrations];
        foreach (var registration in _registrations)
            registration.Register(this);
    }

    public void Add(string hwTypeName, Func<DeviceCreateContext, IDeviceEmulator?> create)
    {
        if (!_factories.TryAdd(hwTypeName, create))
            throw new InvalidOperationException($"Device type '{hwTypeName}' is already registered.");
    }

    public void AddSection(string sectionName, Func<XElement, string, IEnumerable<IDeviceEmulator>> load)
    {
        if (!_sections.TryAdd(sectionName, load))
            throw new InvalidOperationException($"Section loader '{sectionName}' is already registered.");
    }

    public bool TryCreate(DeviceCreateContext ctx, out IDeviceEmulator? device)
    {
        if (_factories.TryGetValue(ctx.ShortType, out var create))
        {
            device = create(ctx);
            return true;
        }
        device = null;
        return false;
    }

    public bool CanCreate(string shortType)
        => _factories.ContainsKey(shortType) || _sections.ContainsKey(shortType);

    public bool TryGetSectionLoader(string sectionName, out Func<XElement, string, IEnumerable<IDeviceEmulator>> load)
    {
        if (_sections.TryGetValue(sectionName, out var found))
        {
            load = found;
            return true;
        }
        load = null!;
        return false;
    }

    public void WireAll(IReadOnlyList<IDeviceEmulator> devices)
    {
        foreach (var registration in _registrations)
            registration.Wire(devices);
    }
}
