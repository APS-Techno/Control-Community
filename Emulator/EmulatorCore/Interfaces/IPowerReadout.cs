namespace EmulatorCore.Interfaces;

public interface IPowerReadout : IDeviceEmulator
{
    string TypeName { get; }
    string VoltageText { get; }
    string CurrentText { get; }
    string PowerText { get; }
    string OutputText { get; }
    bool? OutputOn { get; }
    void ToggleOutput();
}
