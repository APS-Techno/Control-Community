namespace APS.Wire;

/// <summary>Where Control's Agent server is and how to authenticate. <paramref name="CertSha256"/>
/// null = trust-on-first-use (logged), otherwise the hex SHA-256 of the server cert to pin.</summary>
public sealed record WireEndpoint(string Host, int Port, string ApiKey, string? CertSha256);
