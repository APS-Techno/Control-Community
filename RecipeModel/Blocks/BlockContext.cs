namespace APS.RecipeModel.Blocks;

public sealed class BlockContext : IBlockContext
{
    public IReadOnlyList<string> SourceNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> ParameterNames { get; init; } = Array.Empty<string>();
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DevicesByCategory { get; init; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public IReadOnlyList<string> DeviceNames(string category)
        => DevicesByCategory.TryGetValue(category, out var v) ? v : Array.Empty<string>();
}
