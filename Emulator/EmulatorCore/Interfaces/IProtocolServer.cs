using EmulatorCore.Models;

namespace EmulatorCore.Interfaces;

public interface IProtocolServer : IDisposable
{
    string Name { get; }
    void Start();
    void Stop();
    bool IsRunning { get; }
    void Flush();
}

public interface IS7ProtocolServer : IProtocolServer
{
    /// <summary>Matches the plcId attribute from EmulatorConfig.xml (e.g. "tm", "c1").</summary>
    string PlcId { get; }
    void RegisterDbRegion(DbRegion region);
    void RegisterMbBit(MbBitRegistration bit);
    void RegisterEbBit(EbBitRegistration bit);
}

public interface ISerialProtocolServer : IProtocolServer
{
    void RegisterHandler(Func<byte[], byte[]> handler);
}
