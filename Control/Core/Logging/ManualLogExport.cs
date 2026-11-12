using System.IO;
using System.Text;

namespace APS.SystemControl.Core.Logging;

/// <summary>Copies a measurement log out of the Data folder as TSV or CSV. The source may still be open for writing.</summary>
public static class ManualLogExport
{
    /// <summary>
    /// Writes every part of a session. One part goes to <paramref name="destination"/>; several go beside it
    /// as <c>&lt;name&gt;_part1</c>, <c>&lt;name&gt;_part2</c>, … so no clock segment is dropped.
    /// </summary>
    /// <returns>The paths written.</returns>
    public static IReadOnlyList<string> Write(IReadOnlyList<string> parts, string destination, bool csv)
    {
        if (parts.Count == 0) throw new ArgumentException("no measurement log to export", nameof(parts));
        string dir = Path.GetDirectoryName(Path.GetFullPath(destination)) ?? ".";
        string stem = Path.GetFileNameWithoutExtension(destination);
        string ext = csv ? ".csv" : ".tsv";
        var sources = new string[parts.Count];
        var targets = new string[parts.Count];
        for (int i = 0; i < parts.Count; i++)
        {
            sources[i] = Path.GetFullPath(parts[i]);
            targets[i] = Path.GetFullPath(parts.Count == 1
                ? Path.ChangeExtension(destination, ext)
                : Path.Combine(dir, $"{stem}_part{i + 1}{ext}"));
        }
        for (int i = 0; i < targets.Length; i++)
            for (int j = 0; j < sources.Length; j++)
                if (string.Equals(targets[i], sources[j], StringComparison.OrdinalIgnoreCase) && (i != j || csv))
                    throw new IOException("The export would overwrite a measurement source. Choose another destination.");
        for (int i = 0; i < targets.Length; i++)
            if (!string.Equals(targets[i], sources[i], StringComparison.OrdinalIgnoreCase))
                Copy(sources[i], targets[i], csv);
        return targets;
    }

    private static void Copy(string source, string target, bool csv)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, ".aps-export-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(input, Encoding.UTF8))
            using (var output = new StreamWriter(temporary, append: false, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (csv) WriteCsvLine(output, line);
                    else output.WriteLine(line);
                }
            }
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void WriteCsvLine(StreamWriter output, string line)
    {
        int start = 0;
        while (true)
        {
            int separator = line.IndexOf('\t', start);
            int end = separator < 0 ? line.Length : separator;
            var field = line.AsSpan(start, end - start);
            if (field.IndexOfAny(',', '"') >= 0)
            {
                output.Write('"');
                foreach (char character in field)
                {
                    if (character == '"') output.Write('"');
                    output.Write(character);
                }
                output.Write('"');
            }
            else output.Write(field);
            if (separator < 0)
            {
                output.WriteLine();
                return;
            }
            output.Write(',');
            start = separator + 1;
        }
    }
}
