namespace APS.RecipeModel.Blocks;

/// <summary>All members may return empty collections; blocks must degrade gracefully to
/// free-text inputs when context data is missing.</summary>
public interface IBlockContext
{
    /// <summary>Addressable source names (name attr, else material) from Sources.xml.</summary>
    IReadOnlyList<string> SourceNames { get; }

    /// <summary>Device names by HWConf category. Category keys are the friendly
    /// labels used by <c>HWConfDeviceReader.ReadDevicesByCategory</c>:
    /// <c>"Valves"</c>, <c>"MFCs"</c>, <c>"Pumps"</c>, <c>"Stages"</c>,
    /// <c>"Sensors"</c>, <c>"Power Supplies"</c>, <c>"Drives"</c>,
    /// <c>"Controllers"</c>. Returns an empty list for unknown categories.</summary>
    IReadOnlyList<string> DeviceNames(string category);

    /// <summary>Recipe parameter names (flat list, no group prefix).</summary>
    IReadOnlyList<string> ParameterNames { get; }
}
