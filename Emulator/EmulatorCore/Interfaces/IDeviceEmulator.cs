using System.Xml.Linq;
using EmulatorCore.Models;

namespace EmulatorCore.Interfaces;

public enum DeviceCategory { Valve, MFC, Pump, Sensor, PowerSupply, Stage, Controller }

public interface IDeviceEmulator
{
    string Id { get; }
    string Description { get; }
    /// <summary>
    /// PLC plcId for S7 devices (e.g. "tm", "c1").
    /// Empty string for serial devices and non-hosted devices.
    /// </summary>
    string Host { get; }
    DeviceCategory Category { get; }
    void Update(float deltaTime, SimulationState state);
    void RegisterProtocol(IProtocolServer server);
    void Reset();
    object[] GetStatus();
}

public interface IDeviceRegistration
{
    void Register(IDeviceRegistry registry);
    void Wire(IReadOnlyList<IDeviceEmulator> devices) { }
}

public interface IDeviceRegistry
{
    void Add(string hwTypeName, Func<DeviceCreateContext, IDeviceEmulator?> create);
    void AddSection(string sectionName, Func<XElement, string, IEnumerable<IDeviceEmulator>> load);
}
