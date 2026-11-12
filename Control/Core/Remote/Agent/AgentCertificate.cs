using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace APS.SystemControl.Core.Remote.Agent;

/// <summary>Persistent self-signed TLS cert for the agent server. The bridge pins its
/// SHA-256 fingerprint; the PFX password is fixed (LAN-grade — auth comes from API keys).</summary>
internal static class AgentCertificate
{
    private const string Password = "aps-agent";

    internal static X509Certificate2 LoadOrCreate(string pfxPath)
    {
        if (File.Exists(pfxPath))
            return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, Password);
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=APS.Control.Agent", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var temp = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        File.WriteAllBytes(pfxPath, temp.Export(X509ContentType.Pfx, Password));
        return X509CertificateLoader.LoadPkcs12FromFile(pfxPath, Password);
    }
}
