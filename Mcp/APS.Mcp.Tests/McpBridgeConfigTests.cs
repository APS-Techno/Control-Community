using APS.Mcp;
using Xunit;

public class McpBridgeConfigTests
{
    [Fact]
    public void Loads_camel_case_json()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mcp_{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """{"host":"10.0.0.5","port":8026,"apiKey":"secret","certSha256":"AABB"}""");
        try
        {
            var cfg = McpBridgeConfig.Load(path);
            Assert.Equal("10.0.0.5", cfg.Host);
            Assert.Equal(8026, cfg.Port);
            Assert.Equal("secret", cfg.ApiKey);
            Assert.Equal("AABB", cfg.CertSha256);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Download_dir_is_optional_and_null_when_absent()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mcp_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, """{"host":"h","port":1,"apiKey":"k"}""");
        try { Assert.Null(McpBridgeConfig.Load(path).DownloadDir); }
        finally { File.Delete(path); }

        File.WriteAllText(path, """{"host":"h","port":1,"apiKey":"k","downloadDir":"D:/aps-runs"}""");
        try { Assert.Equal("D:/aps-runs", McpBridgeConfig.Load(path).DownloadDir); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Missing_file_throws_with_path_in_message()
    {
        string path = Path.Combine(Path.GetTempPath(), $"none_{Guid.NewGuid():N}.json");
        var ex = Assert.Throws<FileNotFoundException>(() => McpBridgeConfig.Load(path));
        Assert.Contains(path, ex.Message);
    }
}
