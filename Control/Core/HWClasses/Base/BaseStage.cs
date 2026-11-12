using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using APS.SystemControl.Core.HWClasses.Base;

namespace APS.SystemControl.BaseHWClasses;

/// <summary>A named, operator-selectable stage position (mm).</summary>
public sealed record StagePreset(string Name, float Position);

public class BaseStage : IBaseComponent
{
    private bool _connected;
    private CancellationTokenSource? _moveCts;

    public BaseStage(IBaseDeviceParams Params)
    {
        this.Params = Params;
        this.Params.Type = DeviceType.Stage;
    }
    public IBaseDeviceParams Params { get; private set; }

    public bool Connected { get => _connected; set { _connected = value; } }

    public void Poll() { if (Connected) _ = Position; }

    public float Position { get { LastPosition = ReadHW(); return LastPosition; } set => WriteHW(value); }

    /// <summary>
    /// Last position read by the background poll. UI widgets must read this
    /// instead of <see cref="Position"/> — pure in-memory, no PLC I/O, so it
    /// cannot block the UI thread and keeps the PLC socket single-reader.
    /// </summary>
    public float LastPosition { get; private set; }

    protected virtual float ReadHW() => 0;

    protected virtual void WriteHW(float newposition) { }

    /// <summary>
    /// Immediately halts the stage in hardware. Default is a no-op; PLC drivers
    /// override this to pulse a dedicated
    /// stop bit. Paired with <see cref="CancelMove"/> so the in-flight
    /// arrival-wait aborts promptly instead of running to its 120 s timeout.
    /// </summary>
    public virtual void Stop() { }

    /// <summary>
    /// Registers the start of a move and returns a token that is cancelled by
    /// <see cref="CancelMove"/> (a Stop) or when the supplied recipe token
    /// aborts. <c>Core.OpStage</c> feeds this to the arrival-waiter.
    /// </summary>
    public CancellationToken BeginMove(CancellationToken linked)
    {
        var old = _moveCts;
        _moveCts = CancellationTokenSource.CreateLinkedTokenSource(linked);
        // The previous move has already completed or been cancelled by the time
        // a new one starts (moves are sequential per stage), so disposing here
        // is safe and keeps the linked-source count from growing.
        old?.Dispose();
        return _moveCts.Token;
    }

    public void CancelMove()
    {
        try { _moveCts?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    // Runtime-editable settings surfaced by the stage popup and persisted back
    // to the <stage> element in HWConf.xml (see Core.SaveStageConfig). Soft
    // limits use NaN to mean "unbounded" so an unset limit never rejects a move.

    /// <summary>Named preset positions the operator can jump to (mm).</summary>
    public List<StagePreset> Presets { get; private set; } = [];

    /// <summary>Reference / loading position used by the Home command (mm).</summary>
    public float HomePosition { get; set; }

    /// <summary>Lower soft limit (mm); <see cref="float.NaN"/> = unbounded.</summary>
    public float SoftMin { get; set; } = float.NaN;

    /// <summary>Upper soft limit (mm); <see cref="float.NaN"/> = unbounded.</summary>
    public float SoftMax { get; set; } = float.NaN;

    /// <summary>Default jog increment for the popup (mm).</summary>
    public float JogStep { get; set; } = 1.0f;

    /// <summary>Optional speed setpoint (units are driver-specific); 0 = unset.</summary>
    public float Speed { get; set; }

    /// <summary>V-memory byte holding the driver stop bit; &lt; 0 = no stop bit.</summary>
    public int StopByte { get; set; } = -1;

    /// <summary>Bit index of the stop bit within <see cref="StopByte"/>; &lt; 0 disables Stop.</summary>
    public int StopBit { get; set; } = -1;

    /// <summary>False when the profile declares the stage electrically present but mechanically unable to move (<c>operational="false"</c>).</summary>
    public bool IsOperational { get; set; } = true;

    /// <summary>
    /// Fires when presets / limits / jog / speed / home change at runtime so the
    /// host can persist them (mirrors <c>BaseStepperDrive.CalibrationChanged</c>).
    /// </summary>
    public event Action<BaseStage>? ConfigChanged;

    public void SetPresets(IEnumerable<StagePreset> presets, bool persist = true)
    {
        Presets = [.. presets];
        if (persist) RaiseConfigChanged();
    }

    public void RaiseConfigChanged() => ConfigChanged?.Invoke(this);

    /// <summary>
    /// Applies the optional extended-config attributes from the stage's
    /// <c>&lt;stage&gt;</c> element. These are non-positional (see
    /// <c>XMLHardware.NonPositionalAttributes</c>) so they never reach the
    /// driver constructor. All parses are InvariantCulture — HWConf.xml always
    /// uses '.' as the decimal point.
    /// </summary>
    public void ApplyConfig(XElement el)
    {
        HomePosition = ReadFloatAttr(el, "homePos", HomePosition);
        SoftMin      = ReadFloatAttr(el, "softMin", SoftMin);
        SoftMax      = ReadFloatAttr(el, "softMax", SoftMax);
        JogStep      = ReadFloatAttr(el, "jogStep", JogStep);
        Speed        = ReadFloatAttr(el, "speed", Speed);
        // "haltByte"/"haltBit" (not "stopBit") to avoid a case-insensitive clash
        // with the turbo pump's positional "stopbit" attribute in the shared
        // XMLHardware.NonPositionalAttributes filter.
        StopByte     = ReadIntAttr(el, "haltByte", StopByte);
        StopBit      = ReadIntAttr(el, "haltBit", StopBit);
        string? operationalAttr = el.Attribute("operational")?.Value;
        if (!string.IsNullOrWhiteSpace(operationalAttr)
            && bool.TryParse(operationalAttr, out bool operational))
            IsOperational = operational;

        string? presetsAttr = el.Attribute("presets")?.Value;
        if (!string.IsNullOrWhiteSpace(presetsAttr))
            Presets = ParsePresets(presetsAttr);
    }

    /// <summary>Serializes the preset list as <c>Name:pos;Name:pos</c> for HWConf.</summary>
    public string SerializePresets()
        => string.Join(';', Presets.Select(p =>
            $"{p.Name}:{p.Position.ToString(CultureInfo.InvariantCulture)}"));

    private static float ReadFloatAttr(XElement el, string name, float fallback)
    {
        string? v = el.Attribute(name)?.Value;
        return !string.IsNullOrWhiteSpace(v)
            && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
            ? f : fallback;
    }

    private static int ReadIntAttr(XElement el, string name, int fallback)
    {
        string? v = el.Attribute(name)?.Value;
        return !string.IsNullOrWhiteSpace(v)
            && int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
            ? i : fallback;
    }

    private static List<StagePreset> ParsePresets(string raw)
    {
        var list = new List<StagePreset>();
        foreach (var token in raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int colon = token.LastIndexOf(':');
            if (colon <= 0) continue;
            string name = token[..colon].Trim();
            if (float.TryParse(token[(colon + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out float pos))
                list.Add(new StagePreset(name, pos));
        }
        return list;
    }
}

public class StageFactory : IPLCDeviceFactory
{
    public IBaseComponent CreateDevice(IPLCController PLC, IBaseDeviceParams Params) => new BaseStage(Params);
}
