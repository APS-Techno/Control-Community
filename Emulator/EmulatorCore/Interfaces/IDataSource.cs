namespace EmulatorCore.Interfaces;

public enum DataSourceMode { Simulation, LogReplay }

public interface IDataSource
{
    DataSourceMode Mode { get; }
    bool TryGetValue(string deviceId, string property, float simTime, out float value);
}
