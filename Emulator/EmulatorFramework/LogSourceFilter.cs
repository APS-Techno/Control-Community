namespace APS.SystemEmulator;

public static class LogSourceFilter
{
    public const string All = "All";

    public static IReadOnlyList<string> Items(IEnumerable<string> entrySources)
    {
        var prefixes = entrySources
            .Select(SourcePrefix)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);
        return [All, .. prefixes];
    }

    public static bool Matches(string filter, string source)
        => filter == All || source.StartsWith(filter, StringComparison.OrdinalIgnoreCase);

    private static string SourcePrefix(string source)
    {
        int colon = source.IndexOf(':');
        return colon < 0 ? source : source[..colon];
    }
}
