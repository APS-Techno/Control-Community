namespace EmulatorCore.Interfaces;

public interface IShutterEmulator : IDeviceEmulator
{
    bool IsHomed { get; }
    float Position { get; }
}
