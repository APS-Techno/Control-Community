namespace APS.SystemControl.Plugin;

/// <summary>
/// Read-only view onto loaded hardware components — lets a menu contributor
/// gate visibility ("only show Holder Offset if a Holder drive exists") and
/// query live state for DynamicCaption.
/// </summary>
public interface IHardwareRegistry
{
    /// <summary>True iff a device of this type+id is loaded and connected.</summary>
    bool Has(DeviceType type, string deviceId);

    /// <summary>
    /// Returns the device object as T, or null if not present or not assignable.
    /// T is conventionally a base class from APS.SystemControl.BaseHWClasses
    /// (e.g. BaseStepperDrive, BasePowerSupply).
    /// </summary>
    T? GetDevice<T>(string deviceId) where T : class;

    IEnumerable<string> ListDevices(DeviceType type);
}
