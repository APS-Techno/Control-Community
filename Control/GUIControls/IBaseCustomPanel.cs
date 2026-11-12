using APS.SystemControl.Core;

namespace APS.SystemControl.Controls.General;

public class CustomEventArgs(object[] data) : EventArgs
{
    public object[] Data { get; private set; } = data;
}

public interface IBaseCustomPanel
{
    string ID { get; set; }
    string Caption { get; set; }
    DeviceType DeviceType { get; }
    void Update(object[] args);
}

/// <summary>
/// The host calls <see cref="ConfigureOps"/> once, immediately after constructing
/// the panel, with Core's chamber keys and the known embedded-recipe ids.
/// </summary>
public interface IRecipeOpsPanel
{
    /// <param name="chamberKeys">Chamber ids known to Core (e.g. TM, LL, C1..C4).</param>
    /// <remarks>May be a lazily-evaluated sequence; implementations should enumerate it once (e.g. materialize to an array) rather than iterate repeatedly.</remarks>
    void ConfigureOps(IReadOnlyCollection<string> chamberKeys, IEnumerable<string> recipeIds);
}

/// <summary>
/// The host calls <see cref="OnHardwareAvailable"/> right after constructing the
/// panel — Core is fully loaded and devices have connected by then.
/// </summary>
public interface IHardwareAwarePanel
{
    void OnHardwareAvailable(APS.SystemControl.Plugin.IHardwareRegistry hardware);
}

/// <summary>
/// The host applies the panel's Panels.xml <c>&lt;animFlangeSeconds&gt;</c>
/// (base value for both drives) and <c>&lt;animHolderSeconds&gt;</c> /
/// <c>&lt;animShutterSeconds&gt;</c> (per-drive overrides) right after
/// constructing the panel, so the sweep speeds can be calibrated per system to
/// each drive's real traverse time — holder and shutter motors run at
/// different velocities.
/// </summary>
public interface IAnimatedSchematicPanel
{
    /// <summary>Seconds for the holder icon to sweep between two adjacent flanges.</summary>
    float HolderFlangeSeconds { set; }

    /// <summary>Seconds for the shutter icon to sweep between two adjacent flanges.</summary>
    float ShutterFlangeSeconds { set; }
}