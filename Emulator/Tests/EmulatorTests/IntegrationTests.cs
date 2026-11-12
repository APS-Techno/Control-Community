using S7Server;
using APS.Emulator.Devices.S7;
using APS.SystemEmulator.Engine;
using EmulatorCore.Interfaces;
using EmulatorCore.Models;

namespace EmulatorTests;

public class IntegrationTests : IDisposable
{
    private readonly SimulationEngine _engine;
    private readonly S7ProtocolServer _s7;
    private const int TestPort = 10103;
    private const string TestAddress = "127.0.0.1";

    public IntegrationTests()
    {
        _engine = new SimulationEngine();
        _s7 = new S7ProtocolServer(TestAddress, TestPort, "IntegrationTestS7");

        _engine.State.SetChamber(new ChamberState
        {
            Id = "Main",
            Type = ChamberType.Process,
            Pressure = 760f,
            Volume = 0.1f,
            BasePressure = 2.2e-4f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });
        _engine.State.SetChamber(new ChamberState
        {
            Id = "Loadlock",
            Type = ChamberType.Loadlock,
            Pressure = 760f,
            Volume = 0.02f,
            BasePressure = 5e-2f,
            BaseLeakRate = 1e-5f,
            LeakRate = 1e-5f,
        });

        _engine.AddDevice(new ValveEmulator("V1", 2, 0, "Valve 1"));
        _engine.AddDevice(new SensorEmulator("pm", 100, "Main", "Pressure", "Main pressure"));
        _engine.AddDevice(new PumpEmulator("SP1", 0, 5, 10f, "Scroll Pump 1",
            chamberId: "Main", gateValveId: ""));
        _engine.AddDevice(new TurboPumpEmulator("TP", 17, 0, 0, 300f, "Turbo Pump",
            chamberId: "Main", gateValveId: "GVM", forelineValveId: "FL"));

        _engine.AddServer(_s7);
        _engine.RegisterDevicesWithServers();
    }

    public void Dispose() => _engine.Dispose();

    [Fact]
    public async Task FullStack_Sharp7ClientReadsSensorPressure()
    {
        _engine.Start();
        await Task.Delay(200); // Let a few ticks run

        var client = new Sharp7.S7Client();
        client.PLCPort = TestPort;
        int result = client.ConnectTo(TestAddress, 0, 0);
        Assert.Equal(0, result);

        try
        {
            byte[] buf = new byte[4];
            result = client.DBRead(1, 100, 4, buf);
            Assert.Equal(0, result);

            float pressure = Sharp7.S7.GetRealAt(buf, 0);
            Assert.True(pressure > 0, $"Pressure should be positive, was {pressure}");
        }
        finally
        {
            client.Disconnect();
            _engine.Stop();
        }
    }
}
