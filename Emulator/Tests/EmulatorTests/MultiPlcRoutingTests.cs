using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;
using S7Server;

namespace EmulatorTests;

/// <summary>
/// Cluster_Demo declares two S7 PLCs (tm + c1). Devices with the same MB:bit address
/// on different PLCs must remain independent — writing via one PLC's server must not
/// trigger callbacks registered against the other PLC's server.
/// </summary>
public class MultiPlcRoutingTests
{
    private sealed class TrackingS7Server : IS7ProtocolServer
    {
        private readonly List<MbBitRegistration> _mbBits = [];
        private readonly byte[] _mbBuffer = new byte[256];

        public string Name { get; }
        public string PlcId { get; }
        public bool IsRunning => false;
        public void Start() { }
        public void Stop() { }
        public void Flush() { }
        public void Dispose() { }
        public void RegisterDbRegion(DbRegion region) { }
        public void RegisterEbBit(EbBitRegistration bit) { }

        public void RegisterMbBit(MbBitRegistration bit)
        {
            _mbBits.Add(bit);
        }

        /// <summary>
        /// Fires the OnWrite callbacks of all bits registered with THIS server.
        /// </summary>
        public void WriteMbBit(int address, int bitIndex, bool value)
        {
            if (value)
                _mbBuffer[address] |= (byte)(1 << bitIndex);
            else
                _mbBuffer[address] &= (byte)~(1 << bitIndex);

            foreach (var bit in _mbBits)
            {
                if (bit.Address == address && bit.BitIndex == bitIndex)
                    bit.OnWrite(value);
            }
        }

        public int RegisteredBitCount => _mbBits.Count;

        public TrackingS7Server(string name, string plcId)
        {
            Name = name;
            PlcId = plcId;
        }
    }

    [Fact]
    public void RegisterDevicesWithServers_RoutesEachDeviceToItsOwnPlc()
    {
        var tmServer = new TrackingS7Server("S7-tm", "tm");
        var c1Server = new TrackingS7Server("S7-c1", "c1");

        // Two valves at the SAME MB address:bit but on DIFFERENT PLCs
        // (this is the exact Cluster_Demo scenario: LL_RL and C1_RL)
        var llRl = new ValveEmulator("LL_RL", mbAddress: 3, bitIndex: 1, "TM roughing line", host: "tm");
        var c1Rl = new ValveEmulator("C1_RL", mbAddress: 3, bitIndex: 1, "C1 roughing line", host: "c1");

        using var engine = new SimulationEngine();
        engine.AddDevice(llRl);
        engine.AddDevice(c1Rl);
        engine.AddServer(tmServer);
        engine.AddServer(c1Server);

        engine.RegisterDevicesWithServers();

        // Each server must have exactly 1 registration (the device on its PLC, not both)
        Assert.Equal(1, tmServer.RegisteredBitCount);
        Assert.Equal(1, c1Server.RegisteredBitCount);
    }

    [Fact]
    public void WritingViaTmServer_OnlyFlipsTmValve_NotC1Valve()
    {
        var tmServer = new TrackingS7Server("S7-tm", "tm");
        var c1Server = new TrackingS7Server("S7-c1", "c1");

        var llRl = new ValveEmulator("LL_RL", mbAddress: 3, bitIndex: 1, "TM roughing line", host: "tm");
        var c1Rl = new ValveEmulator("C1_RL", mbAddress: 3, bitIndex: 1, "C1 roughing line", host: "c1");

        using var engine = new SimulationEngine();
        engine.AddDevice(llRl);
        engine.AddDevice(c1Rl);
        engine.AddServer(tmServer);
        engine.AddServer(c1Server);

        engine.RegisterDevicesWithServers();

        tmServer.WriteMbBit(address: 3, bitIndex: 1, value: true);

        Assert.True(llRl.IsOpen, "LL_RL should be open after write via TM server");

        Assert.False(c1Rl.IsOpen, "C1_RL must NOT open when only TM server received the write");
    }

    [Fact]
    public void WritingViaC1Server_OnlyFlipsC1Valve_NotTmValve()
    {
        var tmServer = new TrackingS7Server("S7-tm", "tm");
        var c1Server = new TrackingS7Server("S7-c1", "c1");

        var llRl = new ValveEmulator("LL_RL", mbAddress: 3, bitIndex: 1, "TM roughing line", host: "tm");
        var c1Rl = new ValveEmulator("C1_RL", mbAddress: 3, bitIndex: 1, "C1 roughing line", host: "c1");

        using var engine = new SimulationEngine();
        engine.AddDevice(llRl);
        engine.AddDevice(c1Rl);
        engine.AddServer(tmServer);
        engine.AddServer(c1Server);

        engine.RegisterDevicesWithServers();

        c1Server.WriteMbBit(address: 3, bitIndex: 1, value: true);

        Assert.False(llRl.IsOpen, "LL_RL must NOT open when only C1 server received the write");
        Assert.True(c1Rl.IsOpen, "C1_RL should be open after write via C1 server");
    }

    [Fact]
    public void SerialDevicesWithEmptyHost_DoNotRegisterWithS7Servers()
    {
        // Serial devices (Host == "") must be skipped entirely for S7 registration.
        var tmServer = new TrackingS7Server("S7-tm", "tm");
        var valve = new ValveEmulator("RL", mbAddress: 1, bitIndex: 0, "Roughing line", host: "tm");
        var serial = new SerialStub();

        using var engine = new SimulationEngine();
        engine.AddDevice(valve);
        engine.AddDevice(serial);
        engine.AddServer(tmServer);

        engine.RegisterDevicesWithServers();

        Assert.Equal(1, tmServer.RegisteredBitCount);
    }

    private sealed class SerialStub : IDeviceEmulator
    {
        public string Id => "Shutter";
        public string Description => "Shutter drive";
        public string Host => "";
        public DeviceCategory Category => DeviceCategory.Controller;
        public void Update(float deltaTime, SimulationState state) { }
        public void RegisterProtocol(IProtocolServer server) { }
        public void Reset() { }
        public object[] GetStatus() => [];
    }
}
