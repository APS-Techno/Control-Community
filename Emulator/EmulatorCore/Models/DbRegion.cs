namespace EmulatorCore.Models;

public class DbRegion
{
    public required string DeviceId { get; init; }
    public int DbNumber { get; init; } = 1;
    public required int Address { get; init; }
    public required int Length { get; init; }
    public required Func<byte[]> OnRead { get; init; }
    public required Action<byte[]> OnWrite { get; init; }

    /// <summary>
    /// When true, <see cref="OnRead"/> is NOT flushed back into the shared DB
    /// buffer on each tick. Use for input/command regions that the controller
    /// writes and the device consumes via <see cref="OnWrite"/> — flushing an
    /// echo over them would clobber a controller's in-progress byte-by-byte
    /// write (the command-write race; see RobotEmulator's command window).
    /// </summary>
    public bool SkipFlush { get; init; }
}
