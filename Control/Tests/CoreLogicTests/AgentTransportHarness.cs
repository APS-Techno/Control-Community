using System.Net.Security;
using System.Net.Sockets;
using System.Text.Json;
using APS.RecipeModel.Validation;
using APS.SystemControl.Core;
using APS.SystemControl.Core.Remote.Agent;

namespace CoreLogicTests;

internal sealed class AgentTransportHarness : IAsyncDisposable
{
    public const string Secret = "secret";
    internal const string SecretHash = "2bb80d537b1da3e38bd30361aa855686bde0eacd7162fef6a25fe97bf527a25b";

    private readonly TestAgentServer _server;
    private readonly TcpClient _tcp;
    private int _nextId = 1;

    private AgentTransportHarness(TestAgentServer server, TcpClient tcp, StreamReader reader, StreamWriter writer, string authReply)
    {
        _server = server;
        _tcp = tcp;
        Reader = reader;
        Writer = writer;
        AuthReply = authReply;
    }

    public StreamReader Reader { get; }

    public StreamWriter Writer { get; }

    public string AuthReply { get; }

    public static TestAgentServer StartServer(IAgentSystemAccess sys, AgentKey key,
        IAgentStreamExtension? stream = null, int samplerMs = 250)
    {
        var pfx = Path.Combine(Path.GetTempPath(), $"agent_{Guid.NewGuid():N}.pfx");
        var cfg = new AgentServerConfig
        {
            Enabled = true, Bind = "127.0.0.1", Port = 0, AllowRecipe = true, AllowManual = true,
            Keys = [key], BusSamplerMs = samplerMs,
        };
        var dispatcher = new AgentDispatcher(sys)
        {
            Capabilities = ["controlMode", .. stream?.Capabilities ?? []],
        };
        var server = new AgentServer(cfg, dispatcher, AgentCertificate.LoadOrCreate(pfx), stream);
        server.Start();
        return new TestAgentServer(server, pfx);
    }

    public static async Task<(StreamReader Reader, StreamWriter Writer, SslStream Ssl, TcpClient Tcp)> ConnectAsync(int port)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync("APS.Control.Agent");
        return (new StreamReader(ssl), new StreamWriter(ssl) { AutoFlush = true }, ssl, tcp);
    }

    public static async Task<(StreamReader Reader, StreamWriter Writer, TcpClient Tcp, string AuthReply)> ConnectAndAuthAsync(int port)
    {
        var (reader, writer, _, tcp) = await ConnectAsync(port);
        await writer.WriteLineAsync($$"""{"v":1,"id":0,"cmd":"auth","args":{"key":"{{Secret}}"} }""");
        return (reader, writer, tcp, (await reader.ReadLineAsync())!);
    }

    public static async Task<AgentTransportHarness> StartAsync(IAgentStreamExtension? stream, int samplerMs = 250)
    {
        var server = StartServer(new EchoSys(), new AgentKey("dev", SecretHash, AgentTier.Manual), stream, samplerMs);
        var (reader, writer, tcp, authReply) = await ConnectAndAuthAsync(server.BoundPort);
        return new AgentTransportHarness(server, tcp, reader, writer, authReply);
    }

    public async Task<string> CallAsync(string cmd, object? args = null)
    {
        int id = _nextId++;
        await SendAsync(id, cmd, args);
        using var cts = new CancellationTokenSource(5000);
        while (true)
        {
            var line = await Reader.ReadLineAsync(cts.Token) ?? throw new IOException("closed");
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("id", out var i) && i.GetInt32() == id) return line;
        }
    }

    public Task SendAsync(int id, string cmd, object? args = null) =>
        Writer.WriteLineAsync(JsonSerializer.Serialize(new { v = 1, id, cmd, args }));

    public ValueTask DisposeAsync()
    {
        _tcp.Dispose();
        _server.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class TestAgentServer(AgentServer server, string pfx) : IDisposable
{
    public int BoundPort => server.BoundPort;

    public void Stop() => server.Stop();

    public void Dispose()
    {
        server.Dispose();
        try { File.Delete(pfx); } catch { }
    }
}

internal sealed class EchoSys : IAgentSystemAccess
{
    public object DescribeMachine() => new { profile = "TEST" };
    public object DescribeHardware() => new { };
    public string? DescribedFolder, SavedFolder;
    public object DescribeRecipes(string? folder) { DescribedFolder = folder; return new { }; }
    public object DescribeDsl() => new { };
    public object DescribeInterlocks() => new { };
    public object DescribeAlarms() => new { };
    public object SystemStatus() => new { pulse = 1 };
    public object DeviceSnapshot() => new { };
    public object TailLog(int count) => Array.Empty<object>();
    public object RecipeStatus() => new { };
    public object ListRuns(int count) => new { runs = Array.Empty<object>() };
    public object ReadRunFile(string runId, string kind, long offset, int maxBytes) => new { };
    public ValidationReport ValidateWithSummary(string c, bool r) => new([], RecipeScriptSummary.Compute(""), null);
    public object SaveRecipe(string n, string c, bool o, string? f) { SavedFolder = f; return new { }; }
    public void StartRecipe(string n, IReadOnlyDictionary<string, string>? p) { }
    public void AbortRecipe() { }
    public void ResolveFaultPause(FaultDecision d) { }
    public void SendDeviceCommand(string t, string id, object[] a) { }
}
