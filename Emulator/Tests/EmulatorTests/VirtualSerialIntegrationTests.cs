using System.IO.Ports;
using System.Text;
using Protocols.Shared;
using VirtualSerialAdapter;
using Xunit;

namespace EmulatorTests;

public class VirtualSerialIntegrationTests
{
    private const string EmuPort = "COM121"; // adapter (emulator) side
    private const string HwPort = "COM21";   // control-SW (HW) side of the pair

    private static bool IsPortOpenable(string portName)
    {
        try
        {
            using var sp = new SerialPort(portName, 9600);
            sp.Open();
            sp.Close();
            return true;
        }
        catch
        {
            return false;
        }
    }

    [SkippableFact]
    public void VirtualSerial_Pair_Roundtrip_Through_Adapter()
    {
        Skip.IfNot(IsPortOpenable(EmuPort),
            $"APS VirtualSerial driver not serving {EmuPort} - skipping.");

        var received = new TaskCompletionSource<byte[]>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var adapter = new VirtualSerialPortAdapter(
            "TestG500", EmuPort, 9600, FramingMode.CrLfTerminated);
        adapter.RegisterHandler(frame =>
        {
            received.TrySetResult(frame);
            return [];
        });

        try
        {
            adapter.Start();
            Assert.True(adapter.IsRunning,
                $"Adapter failed to open {EmuPort} ({adapter.Status}).");

            using var hw = new SerialPort(HwPort, 9600, Parity.None, 8, StopBits.One);
            hw.Open();
            byte[] ping = Encoding.ASCII.GetBytes("PING\r\n");
            hw.Write(ping, 0, ping.Length);

            Assert.True(received.Task.Wait(TimeSpan.FromSeconds(5)),
                "Adapter did not receive the framed command within 5s.");
            Assert.Equal("PING\r\n",
                Encoding.ASCII.GetString(received.Task.Result));
        }
        finally
        {
            adapter.Stop();
        }
    }
}
