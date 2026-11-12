using System.IO.Ports;
using APS.SystemControl.Core.HWClasses.Base;

namespace CoreLogicTests;

/// <summary>
/// The genuine wedge (a port whose peer has vanished) can only be reproduced on
/// real/virtual hardware, so it is verified on the bench, not here.
/// </summary>
public class SerialWedgeBreakerTests
{
    [Fact]
    public void CloseSafely_NullPort_ReturnsTrue()
    {
        Assert.True(SerialWedgeBreaker.CloseSafely(null, "test"));
    }

    [Fact]
    public void CloseSafely_UnopenedPort_ReturnsTrueWithoutThrowing()
    {
        using var port = new SerialPort("COM200");
        Assert.True(SerialWedgeBreaker.CloseSafely(port, "test"));
        Assert.False(port.IsOpen);
    }

    [Fact]
    public void TryGetPortHandle_UnopenedPort_ReturnsFalseGracefully()
    {
        using var port = new SerialPort("COM200");   // no internal stream until Open()
        bool found = SerialWedgeBreaker.TryGetPortHandle(port, out var handle);
        Assert.False(found);
        Assert.Null(handle);
    }

    [Fact]
    public void OpenPort_HandleIsLocatedAndClosesCleanly()
    {
        // Opportunistic: needs an actually-openable port (dev box virtual pair).
        // Skip silently when none is available so CI stays green.
        var port = TryOpenAnyPort();
        if (port is null)
            return;

        using (port)
        {
            Assert.True(SerialWedgeBreaker.TryGetPortHandle(port, out var handle));
            Assert.NotNull(handle);
            Assert.False(handle!.IsInvalid);
            Assert.False(handle.IsClosed);

            Assert.True(SerialWedgeBreaker.CloseSafely(port, "loopback"));
            Assert.False(port.IsOpen);
        }
    }

    /// <summary>
    /// Callers own the returned (open) port.
    /// </summary>
    private static SerialPort? TryOpenAnyPort()
    {
        foreach (var name in SerialPort.GetPortNames())
        {
            var port = new SerialPort(name) { ReadTimeout = 200, WriteTimeout = 200 };
            try
            {
                port.Open();
                return port;
            }
            catch
            {
                port.Dispose();   // in use / no permission — try the next
            }
        }
        return null;
    }
}
