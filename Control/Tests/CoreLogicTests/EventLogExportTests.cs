using APS.SystemControl.Core.Logging;
using Microsoft.VisualBasic.FileIO;

namespace CoreLogicTests;

public sealed class EventLogExportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aps-events-" + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Messages_with_separators_quotes_and_line_breaks_round_trip(bool csv)
    {
        string path = Path.Combine(directory, csv ? "e.csv" : "e.tsv");
        string tricky = "a\tb, \"c\"\r\nline2\nline3";
        EventLogExport.Write(path, [("2026-10-15", "12:00:00", "Sender", tricky), ("2026-10-15", "12:00:01", "S2", null)], csv);

        using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(csv ? "," : "\t");
        Assert.Equal(["Date", "Time", "Sender", "Message"], parser.ReadFields());
        Assert.Equal(["2026-10-15", "12:00:00", "Sender", tricky], parser.ReadFields());
        Assert.Equal(["2026-10-15", "12:00:01", "S2", ""], parser.ReadFields());
        Assert.True(parser.EndOfData);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
