using APS.SystemControl.Core.Logging;
using Microsoft.VisualBasic.FileIO;
using System.Runtime.InteropServices;

namespace CoreLogicTests;

public sealed class ManualLogExportIntegrityTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aps-export-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Saving_a_measurement_to_its_current_path_does_not_erase_it()
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "measurement.tsv");
        string content = "Time\tElapsed\tS_pm\nHH:mm:ss\ts\tTorr\n12:00:00\t0\t0.002\n";
        File.WriteAllText(path, content);
        ManualLogExport.Write([path], path, csv: false);
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public void Csv_export_preserves_column_names_with_commas_and_quotes()
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "measurement.tsv");
        string destination = Path.Combine(directory, "measurement.csv");
        File.WriteAllText(source, "Time\tElapsed\tpressure, main\tvalve\"state\nHH:mm:ss\ts\tTorr\t1=open\n12:00:00\t0\t0.002\t1\n");
        ManualLogExport.Write([source], destination, csv: true);
        using var parser = new TextFieldParser(destination) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        Assert.Equal(new[] { "Time", "Elapsed", "pressure, main", "valve\"state" }, parser.ReadFields());
        Assert.Equal(new[] { "HH:mm:ss", "s", "Torr", "1=open" }, parser.ReadFields());
        Assert.Equal(new[] { "12:00:00", "0", "0.002", "1" }, parser.ReadFields());
    }

    [Fact]
    public void Exporting_through_a_hard_link_does_not_truncate_the_measurement_source()
    {
        Directory.CreateDirectory(directory);
        string source = Path.Combine(directory, "source.tsv");
        string alias = Path.Combine(directory, "alias.tsv");
        string content = "Time\tElapsed\tS_pm\nHH:mm:ss\ts\tTorr\n12:00:00\t0\t0.002\n";
        File.WriteAllText(source, content);
        Assert.True(CreateHardLink(alias, source, IntPtr.Zero));
        ManualLogExport.Write([source], alias, csv: false);
        Assert.Equal(content, File.ReadAllText(source));
        Assert.Equal(content.Replace("\n", Environment.NewLine), File.ReadAllText(alias));
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, IntPtr securityAttributes);

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
