using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text.Json;
using APS.RecipeModel.Validation;
using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentServerTransportTests
{
    private TestAgentServer StartServer(out int port) =>
        StartServer(out port, new EchoSys(), new AgentKey("dev", AgentTransportHarness.SecretHash, AgentTier.Manual));

    private TestAgentServer StartServer(out int port, EchoSys sys, AgentKey key)
    {
        var server = AgentTransportHarness.StartServer(sys, key);
        port = server.BoundPort;
        return server;
    }

    // Guards the session wiring end to end: dropping the folder in AgentServer's HandleAuth or
    // Handle call would still compile (both have folder-less forms) and silently unscope every key.
    [Fact(Timeout = 15000)]
    public async Task Scoped_session_passes_its_recipe_folder_to_upload_and_listing()
    {
        var sys = new EchoSys();
        using var server = StartServer(out int port, sys,
            new AgentKey("agent", AgentTransportHarness.SecretHash, AgentTier.Recipe, "Agent-Paper2"));
        var (reader, writer, _, tcp) = await AgentTransportHarness.ConnectAsync(port);
        using (tcp)
        {
            await writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
            Assert.True(JsonDocument.Parse((await reader.ReadLineAsync())!).RootElement.GetProperty("ok").GetBoolean());
            await writer.WriteLineAsync("""{"v":1,"id":1,"cmd":"recipe.upload","args":{"name":"P2-01","content":"x"}}""");
            Assert.True(JsonDocument.Parse((await reader.ReadLineAsync())!).RootElement.GetProperty("ok").GetBoolean());
            await writer.WriteLineAsync("""{"v":1,"id":2,"cmd":"describe.recipes"}""");
            Assert.True(JsonDocument.Parse((await reader.ReadLineAsync())!).RootElement.GetProperty("ok").GetBoolean());
        }
        Assert.Equal("Agent-Paper2", sys.SavedFolder);
        Assert.Equal("Agent-Paper2", sys.DescribedFolder);
    }

    [Fact(Timeout = 15000)]
    public async Task Auth_then_request_round_trip()
    {
        using var server = StartServer(out int port);
        var (reader, writer, _, tcp) = await AgentTransportHarness.ConnectAsync(port);
        using (tcp)
        {
            await writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
            var auth = JsonDocument.Parse((await reader.ReadLineAsync())!);
            Assert.True(auth.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("manual", auth.RootElement.GetProperty("data").GetProperty("tier").GetString());

            await writer.WriteLineAsync("""{"v":1,"id":1,"cmd":"status.system"}""");
            var resp = JsonDocument.Parse((await reader.ReadLineAsync())!);
            Assert.True(resp.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, resp.RootElement.GetProperty("data").GetProperty("pulse").GetInt32());
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Bad_key_gets_auth_failed_then_connection_closes()
    {
        using var server = StartServer(out int port);
        var (reader, writer, _, tcp) = await AgentTransportHarness.ConnectAsync(port);
        using (tcp)
        {
            await writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"wrong"}}""");
            var resp = JsonDocument.Parse((await reader.ReadLineAsync())!);
            Assert.Equal("auth_failed",
                resp.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Null(await reader.ReadLineAsync());   // server closed the stream
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Stop_terminates_while_client_connected()
    {
        var server = StartServer(out int port);
        var (reader, writer, _, tcp) = await AgentTransportHarness.ConnectAsync(port);
        using (tcp)
        {
            await writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
            await reader.ReadLineAsync();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            server.Stop();          // must not hang on the parked read
            sw.Stop();
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"Stop took {sw.Elapsed}");
        }
        server.Dispose();
    }

    [Fact(Timeout = 15000)]
    public async Task Plain_tcp_prober_does_not_kill_the_accept_loop()
    {
        using var server = StartServer(out int port);
        using (var probe = new TcpClient())
        {
            await probe.ConnectAsync("127.0.0.1", port);
            probe.GetStream().Write("GET / HTTP/1.0\r\n\r\n"u8);
        }                                                    // dies during TLS handshake
        var (reader, writer, _, tcp) = await AgentTransportHarness.ConnectAsync(port);   // server must still accept
        using (tcp)
        {
            await writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
            Assert.NotNull(await reader.ReadLineAsync());
        }
    }

    [Fact(Timeout = 20000)]
    public async Task Oversized_line_aborts_connection_but_server_stays_up()
    {
        using var server = StartServer(out int port);

        // Connect, complete TLS, then send a single ~5 MB line with no terminating '\n' —
        // the bounded reader must abort once past its cap even without a newline, closing
        // the connection rather than growing an unbounded in-memory line.
        var tcp = new TcpClient();
        await tcp.ConnectAsync("127.0.0.1", port);
        using (tcp)
        {
            var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
            await ssl.AuthenticateAsClientAsync("APS.Control.Agent");
            var reader = new StreamReader(ssl);
            var garbage = new string('x', 5_000_000);
            var bytes = System.Text.Encoding.UTF8.GetBytes(garbage);

            // Drain-tolerant: the server can close the socket the moment it crosses the
            // cap, which can surface as a failure on this side's write (connection reset
            // mid-send) just as easily as on the read (null / thrown on read-back) —
            // either counts as "closed", matching how the existing 5th-connection test
            // treats a refusal.
            bool closed;
            try
            {
                await ssl.WriteAsync(bytes);
                await ssl.FlushAsync();
                closed = await reader.ReadLineAsync() is null;
            }
            catch (IOException) { closed = true; }
            Assert.True(closed, "server should have closed the connection past the line-length cap");
        }

        // The server itself must still be healthy: a fresh client can still connect and auth.
        var (freshReader, freshWriter, _, freshTcp) = await AgentTransportHarness.ConnectAsync(port);
        using (freshTcp)
        {
            await freshWriter.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
            var auth = JsonDocument.Parse((await freshReader.ReadLineAsync())!);
            Assert.True(auth.RootElement.GetProperty("ok").GetBoolean());
        }
    }

    [Fact(Timeout = 15000)]
    public async Task Fifth_concurrent_connection_is_refused()
    {
        using var server = StartServer(out int port);
        var held = new List<(StreamReader Reader, StreamWriter Writer, SslStream Ssl, TcpClient Tcp)>();
        try
        {
            // Saturate the 4-connection cap: each iteration awaits the auth response, so by
            // the time all 4 are authed, the accept loop has already taken the semaphore slot
            // for each of them (that happens synchronously before HandleClientAsync starts).
            for (int i = 0; i < 4; i++)
            {
                var conn = await AgentTransportHarness.ConnectAsync(port);
                await conn.Writer.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
                var auth = JsonDocument.Parse((await conn.Reader.ReadLineAsync())!);
                Assert.True(auth.RootElement.GetProperty("ok").GetBoolean());
                held.Add(conn);
            }

            // The 5th TCP connect is accepted by the OS/listener backlog, but the server sees
            // it is over capacity and disposes the socket immediately (before TLS even starts).
            // Depending on timing that surfaces as a failed/aborted TLS handshake, or a
            // handshake that completes but then reads null on the very first line — accept
            // either as "refused".
            bool refused = false;
            try
            {
                using var tcp5 = new TcpClient();
                await tcp5.ConnectAsync("127.0.0.1", port);
                using var ssl5 = new SslStream(tcp5.GetStream(), leaveInnerStreamOpen: false, (_, _, _, _) => true);
                await ssl5.AuthenticateAsClientAsync("APS.Control.Agent");
                var reader5 = new StreamReader(ssl5);
                var writer5 = new StreamWriter(ssl5) { AutoFlush = true };
                await writer5.WriteLineAsync("""{"v":1,"id":0,"cmd":"auth","args":{"key":"secret"}}""");
                string? line = await reader5.ReadLineAsync();
                refused = line is null;
            }
            catch
            {
                refused = true;   // connect/handshake itself failed — also counts as refused
            }
            Assert.True(refused, "5th connection should have been refused by the over-capacity cap");

            // The 4 held connections must still be alive and functional — the cap must not
            // have disturbed the already-tracked clients.
            var (r1, w1, _, _) = held[0];
            await w1.WriteLineAsync("""{"v":1,"id":1,"cmd":"status.system"}""");
            var resp = JsonDocument.Parse((await r1.ReadLineAsync())!);
            Assert.True(resp.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(1, resp.RootElement.GetProperty("data").GetProperty("pulse").GetInt32());
        }
        finally
        {
            foreach (var (_, _, _, tcp) in held) { try { tcp.Dispose(); } catch { } }
        }
    }

    [Fact(Timeout = 15000)]
    public async Task BusSubscribe_WithoutStreamExtension_ReturnsUnknownCommand()
    {
        await using var harness = await AgentTransportHarness.StartAsync(stream: null);
        var reply = await harness.CallAsync("bus.subscribe", new { filter = "*", minIntervalMs = 250, snapshot = true });
        Assert.Contains("\"unknown_command\"", reply);
    }
}
