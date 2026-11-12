using System.Net;
using System.Net.Sockets;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using Protocols.Shared;

namespace TcpSerialServer;

public class TcpSerialProtocolServer : ISerialProtocolServer
{
    private readonly string _name;
    private readonly string _bindAddress;
    private readonly int _port;
    private readonly FramingMode _framing;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Func<byte[], byte[]>? _handler;

    public string Name => _name;
    public bool IsRunning { get; private set; }

    public TcpSerialProtocolServer(string name, string bindAddress, int port, FramingMode framing)
    {
        _name = name;
        _bindAddress = bindAddress;
        _port = port;
        _framing = framing;
    }

    public void RegisterHandler(Func<byte[], byte[]> handler) => _handler = handler;

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Parse(_bindAddress), _port);
        _listener.Start();
        IsRunning = true;
        _ = AcceptClientsAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Stop();
        IsRunning = false;
    }

    public void Flush() { }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(ct);
                _ = HandleClientAsync(client, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            EmulatorLog.Log(LogLevel.Info, Name, $"Client connected from {client.Client.RemoteEndPoint}");
            var stream = client.GetStream();
            var buffer = new byte[4096];
            var lineBuffer = new List<byte>();

            while (!ct.IsCancellationRequested && client.Connected)
            {
                try
                {
                    int read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
                    if (read == 0) break;

                    for (int i = 0; i < read; i++)
                    {
                        lineBuffer.Add(buffer[i]);

                        byte[]? frame = FrameParser.TakeFrame(lineBuffer, _framing, out int discarded);
                        if (discarded > 0)
                            EmulatorLog.Log(LogLevel.Warning, Name,
                                $"Resync: discarded {discarded} byte(s) that could not start a {_framing} frame");
                        if (frame != null)
                        {
                            EmulatorLog.Log(LogLevel.Debug, Name, $"Command received ({frame.Length} bytes)");

                            if (_handler != null)
                            {
                                byte[] response = _handler(frame);
                                if (response.Length > 0)
                                    await stream.WriteAsync(response, ct);
                            }
                        }
                    }
                }
                catch { break; }
            }
            EmulatorLog.Log(LogLevel.Info, Name, "Client disconnected");
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
