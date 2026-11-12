using APS.SystemControl.Core.HWClasses.Base;
using APS.SystemControl.Plugin;
using CoreApp = APS.SystemControl.Core.Core;

namespace APS.SystemControl.Core.Menus.Services;

internal sealed class HardwareRegistryAdapter : IHardwareRegistry
{
    private readonly CoreApp _core;
    public HardwareRegistryAdapter(CoreApp core) { _core = core; }

    public bool Has(DeviceType type, string deviceId)
    {
        var dev = _core.FindElement(type, deviceId);
        return dev is { Connected: true };
    }

    public T? GetDevice<T>(string deviceId) where T : class
    {
        // Search across all device types — caller's T narrows the match.
        foreach (var kv in _core.hwElements.Item)
        {
            if (kv.Value is null) continue;
            var hit = _core.FindElement(kv.Key, deviceId);
            if (hit is T typed) return typed;
        }
        return null;
    }

    public IEnumerable<string> ListDevices(DeviceType type)
    {
        if (_core.hwElements.Item.TryGetValue(type, out var elements) && elements != null)
            foreach (var kvp in elements)
                yield return kvp.Value.Params.ID;
    }
}
