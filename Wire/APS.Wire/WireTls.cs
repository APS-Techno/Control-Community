using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace APS.Wire;

/// <summary>TCP + TLS connect to Control's Agent server with SHA-256 certificate pinning. Every failure
/// leaves as a <see cref="WireException"/> naming the endpoint and the likely cause — the texts are
/// what operators read, keep them.</summary>
public static class WireTls
{
    public const string TargetHost = "APS.Control.Agent";

    /// <param name="log">Receives operator-facing notices (unpinned fingerprint, refused cert). The caller
    /// adds its own prefix.</param>
    public static async Task<SslStream> ConnectAsync(string host, int port, string? certSha256,
        Action<string> log, CancellationToken ct)
    {
        string endpoint = $"{host}:{port}";
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, ct);
        }
        catch (SocketException ex)
        {
            tcp.Dispose();
            throw new WireException("unreachable",
                $"cannot reach APS Control at {endpoint} ({DescribeSocketError(host, port, ex)})", ex);
        }

        string? refusal = null;
        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, cert, _, _) =>
        {
            var (ok, why) = Validate(cert, certSha256, endpoint, log);
            refusal = why;
            return ok;
        });
        try
        {
            await ssl.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions { TargetHost = TargetHost }, ct);
        }
        catch (AuthenticationException ex)
        {
            string why = refusal
                ?? $"TLS handshake with {endpoint} failed: {ex.Message} — is that port really " +
                   "Control's Agent server (it speaks TLS), or is it some other service?";
            ssl.Dispose(); tcp.Dispose();
            throw new WireException("tls", why, ex);
        }
        catch (IOException ex)
        {
            ssl.Dispose(); tcp.Dispose();
            throw new WireException("tls",
                $"connection to APS Control at {endpoint} dropped during the TLS handshake " +
                $"({ex.Message}) — is that port really Control's Agent server?", ex);
        }
        return ssl;
    }

    public static string DescribeSocketError(string host, int port, SocketException ex) => ex.SocketErrorCode switch
    {
        SocketError.ConnectionRefused =>
            "connection refused — is APS.SystemControl.exe running, and is its Agent server " +
            $"enabled on port {port}?",
        SocketError.TimedOut =>
            "connect timed out — firewall blocking the port, or wrong host?",
        SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain =>
            $"host '{host}' not found — check the host in the bridge config",
        SocketError.HostUnreachable or SocketError.NetworkUnreachable =>
            $"host '{host}' unreachable — network route missing, or wrong host?",
        _ => $"{ex.SocketErrorCode}: {ex.Message}",
    };

    private static (bool Ok, string? Refusal) Validate(X509Certificate? cert, string? pinned, string endpoint,
        Action<string> log)
    {
        if (cert is null)
            return (false, $"APS Control at {endpoint} presented no server certificate");
        string actual = Convert.ToHexString(SHA256.HashData(cert.GetRawCertData()));
        if (string.IsNullOrWhiteSpace(pinned))
        {
            // Trust-on-first-use: accept, but tell the operator what to pin.
            log($"WARNING no certSha256 pinned; server cert fingerprint is {actual}. " +
                "Add it to the bridge config to pin.");
            return (true, null);
        }
        bool ok = string.Equals(actual, pinned.Replace(":", ""), StringComparison.OrdinalIgnoreCase);
        if (ok) return (true, null);
        log($"REFUSED server cert {actual}; pinned {pinned}.");
        return (false,
            $"server certificate of APS Control at {endpoint} has SHA256 {actual}, which does " +
            $"not match the pinned certSha256 {pinned} — Control reinstalled or " +
            "pointed at a different machine? Update certSha256 in the bridge config.");
    }
}
