using EmulatorCore.Interfaces;

namespace EmulatorCore.Models;

public class NullDataSource : IDataSource
{
    public DataSourceMode Mode => DataSourceMode.Simulation;

    public bool TryGetValue(string deviceId, string property, float simTime, out float value)
    {
        value = 0f;
        return false;
    }
}
