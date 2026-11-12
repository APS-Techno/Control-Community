using EmulatorCore.Interfaces;

namespace APS.SystemEmulator.Engine;

public record ColumnMapping(string ColumnName, string Unit, string DeviceId, string Property);

public class LogReplaySource : IDataSource
{
    private string[] _columnNames = [];
    private string[] _columnUnits = [];
    private float[] _timestamps = [];
    private float[][] _values = [];
    private readonly Dictionary<(string DeviceId, string Property), int> _mappings = new();

    public DataSourceMode Mode => DataSourceMode.LogReplay;
    public int ColumnCount => _columnNames.Length;
    public int RowCount => _timestamps.Length;
    public float Duration => _timestamps.Length > 0 ? _timestamps[^1] : 0f;
    public IReadOnlyList<string> ColumnNames => _columnNames;
    public IReadOnlyList<string> ColumnUnits => _columnUnits;

    public void Load(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        if (lines.Length < 3)
            throw new InvalidOperationException("Log file must have at least 3 rows (names, units, data)");

        _columnNames = lines[0].Split('\t');
        _columnUnits = lines[1].Split('\t');

        int colCount = _columnNames.Length;

        var rawRows = new List<(string TimeStr, float[] Values)>();
        for (int i = 2; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (string.IsNullOrEmpty(line)) continue;

            string[] parts = line.Split('\t');
            if (parts.Length < 2) continue;

            string timeStr = parts[0];
            float[] rowValues = new float[colCount];

            for (int c = 1; c < Math.Min(parts.Length, colCount); c++)
            {
                if (float.TryParse(parts[c], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float val))
                {
                    rowValues[c] = val;
                }
            }

            rawRows.Add((timeStr, rowValues));
        }

        // Convert timestamps, distributing duplicate times evenly within each second
        var timestamps = new List<float>();
        var values = new List<float[]>();

        int idx = 0;
        while (idx < rawRows.Count)
        {
            float baseTime = ParseTimeToSeconds(rawRows[idx].TimeStr);

            int count = 1;
            while (idx + count < rawRows.Count && rawRows[idx + count].TimeStr == rawRows[idx].TimeStr)
                count++;

            for (int j = 0; j < count; j++)
            {
                float offset = count > 1 ? (float)j / count : 0f;
                timestamps.Add(baseTime + offset);
                values.Add(rawRows[idx + j].Values);
            }

            idx += count;
        }

        _timestamps = timestamps.ToArray();
        _values = values.ToArray();
    }

    public void SetMappings(List<ColumnMapping> mappings)
    {
        _mappings.Clear();
        foreach (var mapping in mappings)
        {
            int colIndex = FindColumnIndex(mapping.ColumnName, mapping.Unit);
            if (colIndex >= 0)
            {
                _mappings[(mapping.DeviceId, mapping.Property)] = colIndex;
            }
        }
    }

    public bool TryGetValue(string deviceId, string property, float simTime, out float value)
    {
        value = 0f;

        if (!_mappings.TryGetValue((deviceId, property), out int colIndex))
            return false;

        if (_timestamps.Length == 0)
            return false;

        if (simTime <= _timestamps[0])
        {
            value = _values[0][colIndex];
            return true;
        }

        if (simTime >= _timestamps[^1])
        {
            value = _values[^1][colIndex];
            return true;
        }

        int lo = 0, hi = _timestamps.Length - 1;
        while (lo < hi - 1)
        {
            int mid = (lo + hi) / 2;
            if (_timestamps[mid] <= simTime)
                lo = mid;
            else
                hi = mid;
        }

        float t0 = _timestamps[lo];
        float t1 = _timestamps[hi];
        float dt = t1 - t0;

        if (dt < 1e-9f)
        {
            value = _values[lo][colIndex];
            return true;
        }

        float fraction = (simTime - t0) / dt;
        float v0 = _values[lo][colIndex];
        float v1 = _values[hi][colIndex];
        value = v0 + (v1 - v0) * fraction;
        return true;
    }

    private int FindColumnIndex(string name, string unit)
    {
        for (int i = 0; i < _columnNames.Length; i++)
        {
            if (string.Equals(_columnNames[i], name, StringComparison.OrdinalIgnoreCase) &&
                i < _columnUnits.Length &&
                string.Equals(_columnUnits[i], unit, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static float ParseTimeToSeconds(string timeStr)
    {
        var parts = timeStr.Split(':');
        if (parts.Length == 3 &&
            int.TryParse(parts[0], out int hours) &&
            int.TryParse(parts[1], out int minutes) &&
            int.TryParse(parts[2], out int seconds))
        {
            return hours * 3600f + minutes * 60f + seconds;
        }

        if (float.TryParse(timeStr, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float val))
        {
            return val;
        }

        return 0f;
    }
}
