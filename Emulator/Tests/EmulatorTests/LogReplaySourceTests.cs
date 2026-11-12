using APS.SystemEmulator.Engine;
using EmulatorCore.Interfaces;

namespace EmulatorTests;

public class LogReplaySourceTests
{
    private string CreateTempLogFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"logreplay_test_{Guid.NewGuid()}.log");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_ParsesHeaderAndFirstRowValue()
    {
        string content = "Time\tPressure\tFlow\n" +
                         "(s)\tTorr\tsccm\n" +
                         "0:00:00\t10.5\t20.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();

            source.Load(path);
            source.SetMappings([
                new ColumnMapping("Pressure", "Torr", "pm", "Pressure"),
            ]);

            Assert.Equal(3, source.ColumnCount);
            Assert.Equal(1, source.RowCount);
            Assert.True(source.TryGetValue("pm", "Pressure", 0f, out float value));
            Assert.Equal(10.5f, value, 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryGetValue_InterpolatesAtMidpoint()
    {
        // t=0 has 10.0, t=2 has 5.0 — expect 7.5 at t=1
        string content = "Time\tPressure\n" +
                         "(s)\tTorr\n" +
                         "0:00:00\t10.0\n" +
                         "0:00:02\t5.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();
            source.Load(path);
            source.SetMappings([
                new ColumnMapping("Pressure", "Torr", "pm", "Pressure"),
            ]);

            bool found = source.TryGetValue("pm", "Pressure", 1f, out float value);

            Assert.True(found);
            Assert.Equal(7.5f, value, 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_DuplicateTimestamps_DistributedEvenly()
    {
        // 2 rows at t=0:00:01 should become t=1.0 and t=1.5
        string content = "Time\tPressure\n" +
                         "(s)\tTorr\n" +
                         "0:00:00\t100.0\n" +
                         "0:00:01\t50.0\n" +
                         "0:00:01\t40.0\n" +
                         "0:00:02\t20.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();
            source.Load(path);
            source.SetMappings([
                new ColumnMapping("Pressure", "Torr", "pm", "Pressure"),
            ]);

            // At t=1.0 exactly, we should get the first duplicate value (50.0)
            Assert.True(source.TryGetValue("pm", "Pressure", 1.0f, out float val1));
            Assert.Equal(50.0f, val1, 0.01f);

            // At t=1.5, we should get the second duplicate value (40.0)
            Assert.True(source.TryGetValue("pm", "Pressure", 1.5f, out float val2));
            Assert.Equal(40.0f, val2, 0.01f);

            // At t=1.25, we should get interpolation between 50 and 40 = 45
            Assert.True(source.TryGetValue("pm", "Pressure", 1.25f, out float valMid));
            Assert.Equal(45.0f, valMid, 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TryGetValue_UnmappedDevice_ReturnsFalse()
    {
        string content = "Time\tPressure\n" +
                         "(s)\tTorr\n" +
                         "0:00:00\t10.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();
            source.Load(path);
            // No mappings set

            Assert.False(source.TryGetValue("pm", "Pressure", 0f, out _));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SetMappings_DisambiguatesByUnit()
    {
        // Two columns named "System" with different units
        string content = "Time\tSystem\tSystem\n" +
                         "(s)\tTorr\t(C)\n" +
                         "0:00:00\t1.5\t25.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();
            source.Load(path);
            source.SetMappings([
                new ColumnMapping("System", "Torr", "pm", "Pressure"),
                new ColumnMapping("System", "(C)", "tmp", "Temperature"),
            ]);

            Assert.True(source.TryGetValue("pm", "Pressure", 0f, out float pressure));
            Assert.Equal(1.5f, pressure, 0.01f);

            Assert.True(source.TryGetValue("tmp", "Temperature", 0f, out float temp));
            Assert.Equal(25.0f, temp, 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Mode_ReturnsLogReplay()
    {
        var source = new LogReplaySource();
        Assert.Equal(DataSourceMode.LogReplay, source.Mode);
    }

    [Fact]
    public void TryGetValue_ClampsToEndpoints()
    {
        string content = "Time\tPressure\n" +
                         "(s)\tTorr\n" +
                         "0:00:01\t10.0\n" +
                         "0:00:03\t30.0\n";

        string path = CreateTempLogFile(content);
        try
        {
            var source = new LogReplaySource();
            source.Load(path);
            source.SetMappings([
                new ColumnMapping("Pressure", "Torr", "pm", "Pressure"),
            ]);

            // Before first timestamp — clamp to first value
            Assert.True(source.TryGetValue("pm", "Pressure", 0f, out float valBefore));
            Assert.Equal(10.0f, valBefore, 0.01f);

            // After last timestamp — clamp to last value
            Assert.True(source.TryGetValue("pm", "Pressure", 100f, out float valAfter));
            Assert.Equal(30.0f, valAfter, 0.01f);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
