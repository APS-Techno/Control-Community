namespace EmulatorCore.Interfaces;

/// <summary>
/// A device whose physical motion should be audible (looping motor sound).
/// Polled by the app's SoundManager (~5 Hz); implementations must be cheap
/// and thread-safe (motion flags are written on sim/serial threads).
/// </summary>
public interface IAudibleMotion
{
    bool IsInMotion { get; }
}
