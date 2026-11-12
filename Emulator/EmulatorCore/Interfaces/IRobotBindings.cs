namespace EmulatorCore.Interfaces;

public interface IValveEmulator
{
    void Command(bool open);
    bool IsOpen { get; }
}

public interface IStageEmulator
{
    void SetSetpoint(float position);
    float Position { get; }
}

public interface IRobotLockable
{
    void SetRobotLockProvider(Func<bool> isLocked);
}
