namespace EmulatorCore.Models;

public class MbBitRegistration
{
    public required string DeviceId { get; init; }
    public required int Address { get; init; }
    public required int BitIndex { get; init; }
    public required Func<bool> OnRead { get; init; }
    public required Action<bool> OnWrite { get; init; }
}
