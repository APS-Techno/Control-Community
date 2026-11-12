using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using APS.Wire;

public class WireSessionTests
{
    private sealed record Pair(Stream Client, StreamReader SrvIn, StreamWriter SrvOut, TcpClient ServerTcp);

    private static async Task<Pair> PairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientTcp = new TcpClient();
        await clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverTcp = await listener.AcceptTcpClientAsync();
        listener.Stop();
        var srv = serverTcp.GetStream();
        return new(clientTcp.GetStream(), new StreamReader(srv), new StreamWriter(srv) { AutoFlush = true }, serverTcp);
    }

    private static async Task<(WireSession S, Pair P)> OpenAsync(string capsJson = """["bus"]""")
    {
        var p = await PairAsync();
        var open = WireSession.OpenAsync(p.Client, "k", CancellationToken.None);
        var req = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement;
        Assert.Equal("auth", req.GetProperty("cmd").GetString());
        Assert.Equal("k", req.GetProperty("args").GetProperty("key").GetString());
        Assert.Equal(1, req.GetProperty("v").GetInt32());
        int id = req.GetProperty("id").GetInt32();
        await p.SrvOut.WriteLineAsync(
            $$$$"""{"ok":true,"id":{{{{id}}}},"data":{"tier":"read","keyName":"dev","protocol":1,"capabilities":{{{{capsJson}}}},"machine":{"machineName":"LAB-1","profile":"Sputter","appVersion":"1.6.0"}}}""");
        return (await open, p);
    }

    [Fact(Timeout = 10000)]
    public async Task Open_authenticates_and_exposes_capabilities()
    {
        var (s, _) = await OpenAsync();
        Assert.True(s.HasCapability("bus"));
        Assert.False(s.HasCapability("gem"));
        Assert.Equal("LAB-1", s.AuthData.GetProperty("machine").GetProperty("machineName").GetString());
    }

    [Fact(Timeout = 10000)]
    public async Task Old_control_without_capabilities_reports_none()
    {
        var (s, _) = await OpenAsync(capsJson: "[]");
        Assert.False(s.HasCapability("bus"));
    }

    [Fact(Timeout = 10000)]
    public async Task Push_before_ack_reaches_Pushes_and_ack_reaches_caller()
    {
        var (s, p) = await OpenAsync();
        var call = s.CallAsync("bus.subscribe", new { filter = new[] { "*" }, minIntervalMs = 1000 });
        var req = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement;
        Assert.Equal("bus.subscribe", req.GetProperty("cmd").GetString());
        Assert.Equal(1000, req.GetProperty("args").GetProperty("minIntervalMs").GetInt32());
        int id = req.GetProperty("id").GetInt32();
        await p.SrvOut.WriteLineAsync("""{"push":"snapshot","seq":5,"utc":"2026-09-06T00:00:00.000Z","values":{"Main/sensor/pm/value":"NaN"}}""");
        await p.SrvOut.WriteLineAsync($$$"""{"ok":true,"id":{{{id}}},"data":{"subscriptionId":"s1","ids":[]}}""");
        var data = await call;
        Assert.Equal("s1", data.GetProperty("subscriptionId").GetString());
        var frame = await s.Pushes.ReadAsync();
        Assert.Equal("snapshot", frame.GetProperty("push").GetString());
        Assert.Equal("NaN", frame.GetProperty("values").GetProperty("Main/sensor/pm/value").GetString());
    }

    [Fact(Timeout = 10000)]
    public async Task Error_reply_throws_WireException_with_code()
    {
        var (s, p) = await OpenAsync();
        var call = s.CallAsync("bus.subscribe", null);
        int id = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement.GetProperty("id").GetInt32();
        await p.SrvOut.WriteLineAsync(
            $$$"""{"ok":false,"id":{{{id}}},"error":{"code":"feature_not_licensed","message":"feature 'remote-data-logging' not licensed"}}""");
        var ex = await Assert.ThrowsAsync<WireException>(() => call);
        Assert.Equal("feature_not_licensed", ex.Code);
        Assert.Contains("remote-data-logging", ex.Message);
    }

    [Fact(Timeout = 10000)]
    public async Task Garbage_line_is_ignored_and_session_survives()
    {
        var (s, p) = await OpenAsync();
        var call = s.CallAsync("status.system", null);
        int id = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement.GetProperty("id").GetInt32();
        await p.SrvOut.WriteLineAsync("this is not json");
        await p.SrvOut.WriteLineAsync($$$"""{"ok":true,"id":{{{id}}},"data":{"pulse":7}}""");
        Assert.Equal(7, (await call).GetProperty("pulse").GetInt32());
    }

    [Fact(Timeout = 10000)]
    public async Task Server_close_fails_pending_call_and_faults_Pushes()
    {
        var (s, p) = await OpenAsync();
        var call = s.CallAsync("status.system", null);
        await p.SrvIn.ReadLineAsync();
        p.ServerTcp.Close();
        await Assert.ThrowsAnyAsync<IOException>(() => call);
        await Assert.ThrowsAnyAsync<IOException>(() => s.Pushes.Completion);
    }

    [Fact(Timeout = 10000)]
    public async Task Bad_key_fails_open()
    {
        var p = await PairAsync();
        var open = WireSession.OpenAsync(p.Client, "wrong", CancellationToken.None);
        int id = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement.GetProperty("id").GetInt32();
        await p.SrvOut.WriteLineAsync($$$"""{"ok":false,"id":{{{id}}},"error":{"code":"auth_failed","message":"bad key"}}""");
        var ex = await Assert.ThrowsAsync<WireException>(() => open);
        Assert.Equal("auth_failed", ex.Code);
    }

    [Fact(Timeout = 10000)]
    public async Task Dispose_completes_Pushes_cleanly()
    {
        var (s, _) = await OpenAsync();
        await s.DisposeAsync();
        await s.Pushes.Completion;
    }

    [Fact(Timeout = 10000)]
    public async Task Actor_is_sent_only_when_given()
    {
        var (s, p) = await OpenAsync();
        var call = s.CallAsync("recipe.start", new { name = "Cleaning" }, actor: "alice");
        var req = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement;
        Assert.Equal("alice", req.GetProperty("actor").GetString());
        await p.SrvOut.WriteLineAsync($$$"""{"ok":true,"id":{{{req.GetProperty("id").GetInt32()}}},"data":{"accepted":true}}""");
        await call;

        var plain = s.CallAsync("status.system", null);
        var req2 = JsonDocument.Parse((await p.SrvIn.ReadLineAsync())!).RootElement;
        Assert.False(req2.TryGetProperty("actor", out _));
        await p.SrvOut.WriteLineAsync($$$"""{"ok":true,"id":{{{req2.GetProperty("id").GetInt32()}}},"data":{}}""");
        await plain;
    }
}
