using APS.SystemControl.Core.Remote.Agent;
using Xunit;

namespace CoreLogicTests;

public class AgentCertificateTests
{
    [Fact]
    public void Creates_pfx_then_reloads_same_certificate()
    {
        string path = Path.Combine(Path.GetTempPath(), $"agent_{Guid.NewGuid():N}.pfx");
        try
        {
            using var first = AgentCertificate.LoadOrCreate(path);
            Assert.True(File.Exists(path));
            Assert.True(first.HasPrivateKey);
            Assert.Contains("APS.Control.Agent", first.Subject);
            using var second = AgentCertificate.LoadOrCreate(path);
            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally { File.Delete(path); }
    }
}
