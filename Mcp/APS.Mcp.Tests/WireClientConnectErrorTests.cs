using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using APS.Mcp;
using APS.Wire;
using Xunit;

/// <summary>A transport failure must leave WireClient as a WireException that names the endpoint
/// and the likely cause. Anything else escapes ControlTools uncaught and the model only ever sees
/// the MCP SDK's bare "An error occurred invoking 'x'" — which is exactly the bug these pin.</summary>
public class WireClientConnectErrorTests
{
    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public async Task Connection_refused_names_endpoint_and_cause()
    {
        int port = FreePort();
        await using var client = new WireClient(new McpBridgeConfig("127.0.0.1", port, "k", null));

        var ex = await Assert.ThrowsAsync<WireException>(() => client.CallAsync("status.system", null));

        Assert.Equal("unreachable", ex.Code);
        Assert.Contains($"127.0.0.1:{port}", ex.Message);
        Assert.Contains("refused", ex.Message);
        Assert.Contains("APS.SystemControl.exe", ex.Message);
        Assert.IsType<SocketException>(ex.InnerException);
    }

    [Fact]
    public async Task Refused_on_a_mutating_command_is_reported_the_same_way()
    {
        int port = FreePort();
        await using var client = new WireClient(new McpBridgeConfig("127.0.0.1", port, "k", null));

        var ex = await Assert.ThrowsAsync<WireException>(() => client.CallAsync("recipe.start", null));

        Assert.Equal("unreachable", ex.Code);
        Assert.Contains($"127.0.0.1:{port}", ex.Message);
    }

    [Fact]
    public async Task Server_closing_during_handshake_is_reported_as_tls()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accept = Task.Run(async () =>
        {
            using var s = await listener.AcceptTcpClientAsync();
            s.Close();
        });
        try
        {
            await using var client = new WireClient(new McpBridgeConfig("127.0.0.1", port, "k", null));
            var ex = await Assert.ThrowsAsync<WireException>(() => client.CallAsync("status.system", null));
            Assert.Equal("tls", ex.Code);
            Assert.Contains($"127.0.0.1:{port}", ex.Message);
        }
        finally { listener.Stop(); await accept; }
    }

    [Fact]
    public async Task Pinned_fingerprint_mismatch_reports_both_fingerprints()
    {
        using var cert = SelfSigned();
        string actual = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
        string pinned = new string('0', 64);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var serve = Task.Run(async () =>
        {
            using var s = await listener.AcceptTcpClientAsync();
            using var ssl = new SslStream(s.GetStream());
            try { await ssl.AuthenticateAsServerAsync(cert); } catch { /* client refuses us */ }
        });
        try
        {
            await using var client = new WireClient(new McpBridgeConfig("127.0.0.1", port, "k", pinned));
            var ex = await Assert.ThrowsAsync<WireException>(() => client.CallAsync("status.system", null));
            Assert.Equal("tls", ex.Code);
            Assert.Contains(actual, ex.Message);
            Assert.Contains(pinned, ex.Message);
            Assert.Contains("certSha256", ex.Message);
        }
        finally { listener.Stop(); await serve; }
    }

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=APS.Control.Agent", rsa, HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        // Windows SChannel needs the private key in a persisted/exportable form to serve with it.
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx), null,
            X509KeyStorageFlags.Exportable);
    }
}
