using System.Net;

namespace APS.Agent.Core.Providers;

public static class EndpointGuard
{
    public static void Validate(Uri uri, bool allowExternalEndpoints)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException($"Endpoint '{uri}' must be an absolute HTTP/HTTPS URI.");

        if (allowExternalEndpoints)
            return;

        if (!IsLoopbackHost(uri.Host))
            throw new InvalidOperationException($"External endpoint '{uri.Host}' blocked by loopback air-gap lock.");
    }

    public static bool IsLoopbackHost(string host)
    {
        string normalized = host.Trim('[', ']');
        if (string.Equals(normalized, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;
        return IPAddress.TryParse(normalized, out var ip) && IPAddress.IsLoopback(ip);
    }

    public static HttpClientHandler CreateNoRedirectHandler() => new()
    {
        AllowAutoRedirect = false,
    };

    public static string? ReadApiKey(string? apiKeyEnvVar) =>
        string.IsNullOrWhiteSpace(apiKeyEnvVar) ? null : Environment.GetEnvironmentVariable(apiKeyEnvVar);
}
