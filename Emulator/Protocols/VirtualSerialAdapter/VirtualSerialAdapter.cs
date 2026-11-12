using System.IO.Ports;
using System.Text.RegularExpressions;
using EmulatorCore.Interfaces;
using EmulatorCore.Logging;
using Protocols.Shared;

namespace VirtualSerialAdapter;

public class VirtualSerialPortAdapter : ISerialProtocolServer
{
    private readonly string _name;
    private readonly string _portName;
    private readonly string _hwPort;   // HW port the control SW connects to (e.g. COM21)
    private readonly int _baudRate;
    private readonly FramingMode _framing;
    private SerialPort? _serialPort;
    private Func<byte[], byte[]>? _handler;
    private readonly List<byte> _rxBuffer = [];
    private readonly Lock _lock = new();

    public string Name => _name;
    public bool IsRunning { get; private set; }
    public string PortName => _portName;
    public string Status { get; private set; } = "Stopped";
    public string HwPort => _hwPort;
    public bool PeerDetected
    {
        get
        {
            try { return _serialPort is { IsOpen: true } sp && sp.DsrHolding; }
            catch { return false; }
        }
    }

    public VirtualSerialPortAdapter(string name, string portName, int baudRate, FramingMode framing)
    {
        _name = name;
        _portName = portName;
        _hwPort = DeriveHwPort(portName);
        _baudRate = baudRate;
        _framing = framing;
    }

    /// <summary>
    /// Derives the HW port name (what control software connects to) from the emulator port.
    /// E.g. "COM121" → "COM21", "COM103" → "COM3".
    /// </summary>
    private static string DeriveHwPort(string emulatorPort)
    {
        var match = Regex.Match(emulatorPort, @"\d+");
        if (match.Success && int.TryParse(match.Value, out int n) && n > 100)
            return $"COM{n - 100}";
        return emulatorPort;
    }

    public void RegisterHandler(Func<byte[], byte[]> handler) => _handler = handler;

    public void Start()
    {
        try
        {
            _serialPort = new SerialPort(_portName, _baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = SerialPort.InfiniteTimeout,
                WriteTimeout = 1000,
                ReadBufferSize = 4096,
                WriteBufferSize = 4096,
            };

            _serialPort.DataReceived += OnDataReceived;
            _serialPort.ErrorReceived += OnErrorReceived;
            _serialPort.Open();

            IsRunning = true;
            Status = "Running";
            EmulatorLog.Log(LogLevel.Info, Name, $"Opened {_portName} (HW pair {_hwPort}) at {_baudRate} baud ({_framing})");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or ArgumentException)
        {
            Status = "Port Missing";
            EmulatorLog.Log(LogLevel.Warning, Name,
                $"Open {_portName} (HW pair {_hwPort}) failed: {ex.GetType().Name}: {ex.Message} — run Virtual Port Setup to (re)create port pairs");
        }
    }

    public void Stop()
    {
        if (_serialPort != null)
        {
            _serialPort.DataReceived -= OnDataReceived;
            _serialPort.ErrorReceived -= OnErrorReceived;

            if (_serialPort.IsOpen)
            {
                try { _serialPort.Close(); }
                catch (IOException) { }
            }

            _serialPort.Dispose();
            _serialPort = null;
        }

        IsRunning = false;
        Status = "Stopped";
        EmulatorLog.Log(LogLevel.Info, Name, $"Closed {_portName} (HW pair {_hwPort})");
    }

    public void Flush() { }

    private void OnDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        var port = _serialPort;
        if (port == null || !port.IsOpen) return;

        try
        {
            int available = port.BytesToRead;
            if (available == 0) return;

            var incoming = new byte[available];
            int read = port.Read(incoming, 0, available);

            List<byte[]> frames = [];
            lock (_lock)
            {
                for (int i = 0; i < read; i++)
                {
                    _rxBuffer.Add(incoming[i]);

                    byte[]? frame = FrameParser.TakeFrame(_rxBuffer, _framing, out int discarded);
                    if (discarded > 0)
                        EmulatorLog.Log(LogLevel.Warning, Name,
                            $"Resync on {_hwPort}: discarded {discarded} byte(s) that could not start a {_framing} frame");
                    if (frame != null)
                        frames.Add(frame);
                }
            }

            foreach (var frame in frames)
            {
                EmulatorLog.Log(LogLevel.Debug, Name,
                    $"Command received ({frame.Length} bytes)");

                if (_handler != null)
                {
                    byte[] response = _handler(frame);
                    if (response.Length > 0)
                    {
                        try
                        {
                            port.Write(response, 0, response.Length);
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or IOException)
                        {
                            EmulatorLog.Log(LogLevel.Warning, Name,
                                $"Write failed on {_hwPort}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            EmulatorLog.Log(LogLevel.Warning, Name, $"Read error on {_hwPort}: {ex.Message}");
        }
    }

    private void OnErrorReceived(object sender, SerialErrorReceivedEventArgs e)
    {
        EmulatorLog.Log(LogLevel.Warning, Name, $"Serial error on {_hwPort}: {e.EventType}");
    }

    public void Dispose()
    {
        Stop();
    }
}
