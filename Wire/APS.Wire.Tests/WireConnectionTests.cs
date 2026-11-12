using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using APS.Wire;
using Xunit;

public class WireConnectionTests
{
    /// <summary>Loopback plain-TCP pair: WireConnection is TLS-agnostic (takes any Stream).</summary>
    private static async Task<(WireConnection Client, StreamReader SrvIn, StreamWriter SrvOut)> PairAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var clientTcp = new TcpClient();
        await clientTcp.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        var serverTcp = await listener.AcceptTcpClientAsync();
        listener.Stop();
        var srv = serverTcp.GetStream();
        return (new WireConnection(clientTcp.GetStream()),
                new StreamReader(srv), new StreamWriter(srv) { AutoFlush = true });
    }

    [Fact(Timeout = 10000)]
    public async Task Call_writes_request_and_returns_matching_data()
    {
        var (client, srvIn, srvOut) = await PairAsync();
        var call = client.CallAsync("status.system", null);
        string sent = (await srvIn.ReadLineAsync())!;
        var req = JsonDocument.Parse(sent).RootElement;
        Assert.Equal("status.system", req.GetProperty("cmd").GetString());
        int id = req.GetProperty("id").GetInt32();
        await srvOut.WriteLineAsync($$$"""{"id":{{{id}}},"ok":true,"data":{"pulse":7}}""");
        var data = await call;
        Assert.Equal(7, data.GetProperty("pulse").GetInt32());
    }

    [Fact(Timeout = 10000)]
    public async Task Error_response_throws_WireException_with_code()
    {
        var (client, srvIn, srvOut) = await PairAsync();
        var call = client.CallAsync("device.command", new { type = "Valve" });
        int id = JsonDocument.Parse((await srvIn.ReadLineAsync())!)
            .RootElement.GetProperty("id").GetInt32();
        await srvOut.WriteLineAsync(
            $$$"""{"id":{{{id}}},"ok":false,"error":{"code":"tier_denied","message":"nope"}}""");
        var ex = await Assert.ThrowsAsync<WireException>(() => call);
        Assert.Equal("tier_denied", ex.Code);
        Assert.Contains("nope", ex.Message);
    }

    [Fact(Timeout = 10000)]
    public async Task Args_are_serialized_camel_case()
    {
        var (client, srvIn, srvOut) = await PairAsync();
        var call = client.CallAsync("recipe.start", new { Name = "Cleaning" });
        var req = JsonDocument.Parse((await srvIn.ReadLineAsync())!).RootElement;
        Assert.Equal("Cleaning", req.GetProperty("args").GetProperty("name").GetString());
        await srvOut.WriteLineAsync(
            $$$"""{"id":{{{req.GetProperty("id").GetInt32()}}},"ok":true,"data":{"accepted":true}}""");
        await call;
    }
}
