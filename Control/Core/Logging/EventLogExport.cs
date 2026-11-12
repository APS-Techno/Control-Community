using System.IO;
using System.Text;

namespace APS.SystemControl.Core.Logging;

public static class EventLogExport
{

    public static void Write(string destination, IEnumerable<(string? Date, string? Time, string? Sender, string? Message)> rows, bool csv)
    {
        char separator = csv ? ',' : '\t';
        string full = Path.GetFullPath(destination);
        string directory = Path.GetDirectoryName(full) ?? ".";
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".aps-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new StreamWriter(temporary, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                WriteRecord(output, "Date", "Time", "Sender", "Message", separator);
                foreach (var row in rows)
                    WriteRecord(output, row.Date, row.Time, row.Sender, row.Message, separator);
            }
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteRecord(StreamWriter output, string? date, string? time, string? sender, string? message, char separator)
    {
        WriteField(output, date, separator);
        output.Write(separator);
        WriteField(output, time, separator);
        output.Write(separator);
        WriteField(output, sender, separator);
        output.Write(separator);
        WriteField(output, message, separator);
        output.Write("\r\n");
    }

    private static void WriteField(StreamWriter output, string? value, char separator)
    {
        string field = value ?? "";
        bool quoted = false;
        foreach (char character in field)
        {
            if (character == separator || character is '"' or '\r' or '\n')
            {
                quoted = true;
                break;
            }
        }
        if (!quoted)
        {
            output.Write(field);
            return;
        }

        output.Write('"');
        ReadOnlySpan<char> remaining = field.AsSpan();
        int quote;
        while ((quote = remaining.IndexOf('"')) >= 0)
        {
            output.Write(remaining[..quote]);
            output.Write("\"\"");
            remaining = remaining[(quote + 1)..];
        }
        output.Write(remaining);
        output.Write('"');
    }
}
