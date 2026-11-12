using System.ComponentModel;
using APS.Controls.Theme;
using APS.Controls.Vacuum;
using APS.Controls.XML.GUI;
using APS.Controls.Vacuum.Schematic;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Controls.Vacuum;

public enum PipeState { Idle, Flowing }

[ToolboxItem(false)]
public class VacuumSchematic : APS.Controls.Vacuum.Schematic.VacuumSchematicCanvas
{
    private record ChamberAnchorDef(string Name, PointF PercentPos);
    private record ChamberDef(
        string Id, string? Label,
        float Left, float Top, float Width, float Height, float Wall,
        List<ChamberAnchorDef> Anchors,
        string? Sensor = null);

    private readonly List<ChamberDef> _chambers = [];
    private readonly Dictionary<string, float> _chamberPressures = [];

    private readonly Dictionary<string, PipeState> _pipeStates = [];
    private readonly HashSet<string> _passiveComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _alwaysFlowingComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _openComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _throttleComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _mixerComponents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _pipeGasCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Color> _pipeColorOverrides = new(StringComparer.OrdinalIgnoreCase);

    public VacuumSchematic()
    {
        BackColor = VacuumTheme.BackgroundDark;
    }

    /// <summary>
    /// Pressure in Torr. The sensor→chamber link is configured via the sensor
    /// attribute on chamber elements in XML.
    /// </summary>
    public void UpdateSensorPressure(string sensorId, float pressureTorr)
    {
        foreach (var ch in _chambers)
        {
            if (ch.Sensor == sensorId)
            {
                // Sensors stream every 200 ms poll. Only repaint when the value
                // actually moved — a pure-noise re-read of an unchanged pressure
                // must not force a full schematic repaint each tick. Tolerance is
                // relative (sub-1% is below display resolution) with an absolute
                // floor for readings near zero.
                if (_chamberPressures.TryGetValue(ch.Id, out float prev)
                    && Math.Abs(prev - pressureTorr) <= Math.Max(1e-12f, 5e-3f * Math.Abs(prev)))
                    return;
                _chamberPressures[ch.Id] = pressureTorr;
                Invalidate();
                return;
            }
        }
    }

    public void LoadFromXml(XML_Schematic? schematic)
    {
        _chambers.Clear();

        if (schematic?.Chambers?.Items != null)
        {
            foreach (var xmlChamber in schematic.Chambers.Items)
            {
                if (string.IsNullOrEmpty(xmlChamber.Id)) continue;
                if (xmlChamber.Width <= 0 || xmlChamber.Height <= 0) continue;

                var anchors = GenerateChamberAnchors(xmlChamber);
                _chambers.Add(new ChamberDef(
                    xmlChamber.Id, xmlChamber.Label,
                    xmlChamber.Left, xmlChamber.Top, xmlChamber.Width, xmlChamber.Height,
                    xmlChamber.Wall,
                    anchors,
                    xmlChamber.Sensor));
            }
        }

        Schematic = schematic;
    }

    /// <summary>
    /// Checks chamber anchors FIRST (before devices) to avoid name-suffix
    /// shadowing (e.g. chamber "C1" vs device "MAG_C1").
    /// </summary>
    public override PointF? ResolveEndpoint(string? spec)
    {
        if (string.IsNullOrEmpty(spec)) return null;

        // Don't intercept @x,y literals — pass straight to base.
        if (!spec.StartsWith('@'))
        {
            var parts = spec.Split(':');
            if (parts.Length == 2)
            {
                string componentId = parts[0];
                string anchorName = parts[1];

                foreach (var chamber in _chambers)
                {
                    if (string.Equals(chamber.Id, componentId, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var anchor in chamber.Anchors)
                        {
                            if (string.Equals(anchor.Name, anchorName, StringComparison.OrdinalIgnoreCase))
                            {
                                var px = PercentToPixel(anchor.PercentPos.X, anchor.PercentPos.Y);
                                return new PointF(px.X, px.Y);
                            }
                        }
                        // Chamber found but anchor not — no point trying devices.
                        return null;
                    }
                }

                // Try base class (ThemedVacuumControl by ID).
                var basePt = base.ResolveEndpoint(spec);
                if (basePt != null) return basePt;

                // Name-suffix fallback for sensors (e.g. "vc_sensor_C1" matches "C1:center").
                foreach (Control control in Controls)
                {
                    if (control.Name != null &&
                        control.Name.EndsWith("_" + componentId, StringComparison.OrdinalIgnoreCase))
                    {
                        if (control is ThemedVacuumControl tvc2)
                        {
                            var pt = tvc2.GetAnchorPoint(anchorName);
                            return new PointF(pt.X, pt.Y);
                        }
                        return new PointF(
                            control.Location.X + control.Width / 2f,
                            control.Location.Y + control.Height / 2f);
                    }
                }

                return null;
            }
        }

        return base.ResolveEndpoint(spec);
    }

    protected override Color? GetPipeOverlayColor(XML_Pipe pipe)
    {
        var pipeId = pipe.Id ?? "";
        var isFlowing = _pipeStates.TryGetValue(pipeId, out var state) && state == PipeState.Flowing;
        if (!isFlowing) return null;

        if (_pipeColorOverrides.TryGetValue(pipeId, out var overrideColor))
            return overrideColor;

        var gas = pipe.Gas
            ?? (_pipeGasCache.TryGetValue(pipeId, out var cached) ? cached : null)
            ?? DeriveGas(pipe.From ?? "");
        return gas != null && GasColors.TryGetValue(gas, out var c) ? c : VacuumTheme.GlowBlue;
    }

    protected override Color GetChamberCavityTint(XML_Chamber chamber, Color baseColor)
    {
        if (!_chamberPressures.TryGetValue(chamber.Id ?? "", out float pTorr) || pTorr <= 1e-8f)
            return baseColor;

        const float pWorking = 0.01f;  // 10 mTorr — zone boundary
        const float pAtm = 760f;
        float cr = baseColor.R, cg = baseColor.G, cb = baseColor.B;

        if (pTorr <= pWorking)
        {
            // Zone 1: ultimate (1e-8) → working (10 mTorr) — bluish glow
            float t = Math.Clamp(MathF.Log10(pTorr / 1e-8f) / MathF.Log10(pWorking / 1e-8f), 0f, 1f);
            cr += t * 4;
            cg += t * 10;
            cb += t * 35;
        }
        else
        {
            // Zone 2: working (10 mTorr) → atmosphere (760) — blue→warm
            float t = Math.Clamp(MathF.Log10(pTorr / pWorking) / MathF.Log10(pAtm / pWorking), 0f, 1f);
            cr += 4 + t * 36;
            cg += 10 + t * 15;
            cb += 35 - t * 25;
        }

        return Color.FromArgb(
            Math.Clamp((int)cr, 0, 255),
            Math.Clamp((int)cg, 0, 255),
            Math.Clamp((int)cb, 0, 255));
    }

    /// <summary>
    /// Flow propagates through passive components (T-connector, air inlet) to adjacent pipes.
    /// </summary>
    public void RegisterPassiveComponent(string componentId) =>
        _passiveComponents.Add(componentId);

    /// <summary>
    /// Gas supply inlets: pipes touching these components are always glowing.
    /// </summary>
    public void RegisterAlwaysFlowing(string componentId) =>
        _alwaysFlowingComponents.Add(componentId);

    /// <summary>
    /// Output pipes will show a blended color when multiple different gases
    /// flow into the mixer's inputs.
    /// </summary>
    public void RegisterMixerComponent(string componentId) =>
        _mixerComponents.Add(componentId);

    /// <summary>
    /// Throttles conduct flow when open
    /// (they stay in the open set, so flow propagates through them) but never seed
    /// flow in <see cref="RecalculateFlow"/> — an open throttle with nothing flowing
    /// into it leaves its pipes idle instead of self-illuminating both sides.
    /// </summary>
    public void RegisterThrottleComponent(string componentId) =>
        _throttleComponents.Add(componentId);

    /// <summary>Test/diagnostic accessor: true when the named pipe is currently Flowing.</summary>
    public bool IsPipeFlowing(string pipeId) =>
        _pipeStates.TryGetValue(pipeId, out var s) && s == PipeState.Flowing;

    /// <summary>
    /// Call once after loading the schematic.
    /// </summary>
    public void ApplyAlwaysFlowing()
    {
        foreach (var id in _alwaysFlowingComponents)
            SetComponentFlowing(id, true);
    }

    public void SetPipeState(string pipeId, PipeState state)
    {
        _pipeStates[pipeId] = state;
        Invalidate();
    }

    public void SetGasFlowing(string gas, bool flowing)
    {
        var items = Schematic?.Pipes?.Items;
        if (items == null) return;
        foreach (var pipe in items)
        {
            if (string.Equals(pipe.Gas, gas, StringComparison.OrdinalIgnoreCase))
                _pipeStates[pipe.Id ?? ""] = flowing ? PipeState.Flowing : PipeState.Idle;
        }
        Invalidate();
    }

    public void SetAllPipesFlowing(bool flowing)
    {
        var items = Schematic?.Pipes?.Items;
        if (items == null) return;
        var state = flowing ? PipeState.Flowing : PipeState.Idle;
        foreach (var pipe in items)
            _pipeStates[pipe.Id ?? ""] = state;
        Invalidate();
    }

    /// <summary>
    /// Marks a component as open (valve/gate open) — flow propagates through it.
    /// Call with false when the component closes.
    /// Returns <c>true</c> when the open-set actually changed, so callers can
    /// skip the expensive <see cref="RecalculateFlow"/> traversal on the common
    /// case where the 200 ms poll re-reports an unchanged state.
    /// </summary>
    public bool SetComponentOpen(string componentId, bool open)
    {
        return open
            ? _openComponents.Add(componentId)
            : _openComponents.Remove(componentId);
    }

    /// <summary>
    /// Sets the flow state for all pipes connected to the specified component,
    /// propagating through passive and open components.
    /// </summary>
    public void SetComponentFlowing(string componentId, bool flowing)
    {
        bool changed = false;
        var newState = flowing ? PipeState.Flowing : PipeState.Idle;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? gas = GetComponentGas(componentId);
        PropagateFlow(componentId, newState, gas, visited, ref changed);
        BlendMixerOutputs();
        if (changed)
            Invalidate();
    }

    /// <summary>
    /// Call after valve or pump state changes.
    /// </summary>
    public void RecalculateFlow()
    {
        var items = Schematic?.Pipes?.Items;
        if (items != null)
        {
            foreach (var pipe in items)
            {
                _pipeStates[pipe.Id ?? ""] = PipeState.Idle;
                _pipeGasCache[pipe.Id ?? ""] = null;
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool changed = false;

        foreach (var id in _alwaysFlowingComponents)
        {
            visited.Clear();
            string? gas = GetComponentGas(id);
            PropagateFlow(id, PipeState.Flowing, gas, visited, ref changed);
        }

        foreach (var id in _openComponents)
        {
            // Throttle valves conduct flow but never originate it. An open throttle with
            // nothing actually flowing into it must not self-illuminate its own pipes
            // (the "flow on both sides between two closed valves" bug). It stays in
            // _openComponents so PropagateFlow still traverses *through* it when a real
            // source (pump/gas) reaches it — it is simply not a seed here.
            if (_throttleComponents.Contains(id)) continue;
            visited.Clear();
            string? gas = GetComponentGas(id);
            PropagateFlow(id, PipeState.Flowing, gas, visited, ref changed);
        }

        BlendMixerOutputs();

        if (changed)
            Invalidate();
    }

    private void PropagateFlow(string componentId, PipeState newState, string? gas,
        HashSet<string> visited, ref bool changed)
    {
        if (!visited.Add(componentId)) return;

        string? componentGas = GetComponentGas(componentId);
        if (componentGas != null) gas = componentGas;

        var items = Schematic?.Pipes?.Items;
        if (items == null) return;

        string prefix = componentId + ":";
        foreach (var pipe in items)
        {
            string from = pipe.From ?? "";
            string to = pipe.To ?? "";
            bool fromMatch = from.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            bool toMatch = to.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            if (!fromMatch && !toMatch) continue;

            string pipeId = pipe.Id ?? "";
            if (!_pipeStates.TryGetValue(pipeId, out var current) || current != newState)
            {
                _pipeStates[pipeId] = newState;
                changed = true;
            }

            if (pipe.Gas != null)
                _pipeGasCache[pipeId] = pipe.Gas;
            else if (gas != null)
                _pipeGasCache[pipeId] = gas;

            string otherSpec = fromMatch ? to : from;
            string otherId = otherSpec.Split(':')[0];
            if (_passiveComponents.Contains(otherId) || _openComponents.Contains(otherId))
                PropagateFlow(otherId, newState, gas, visited, ref changed);
        }
    }

    private string? GetComponentGas(string componentId)
    {
        foreach (Control control in Controls)
        {
            if (control is MFCControl mfc &&
                string.Equals(mfc.ID, componentId, StringComparison.OrdinalIgnoreCase))
                return mfc.Gas;
            if (control is GasInletControl inlet &&
                string.Equals(inlet.ID, componentId, StringComparison.OrdinalIgnoreCase))
                return inlet.Gas;
        }
        return null;
    }

    private void BlendMixerOutputs()
    {
        _pipeColorOverrides.Clear();

        var items = Schematic?.Pipes?.Items;
        if (items == null) return;

        foreach (var mixerId in _mixerComponents)
        {
            string prefix = mixerId + ":";
            var inputColors = new List<Color>();
            string? lastGas = null;
            bool multipleGases = false;

            foreach (var pipe in items)
            {
                string to = pipe.To ?? "";
                if (!to.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string pipeId = pipe.Id ?? "";
                if (!(_pipeStates.TryGetValue(pipeId, out var state) && state == PipeState.Flowing)) continue;

                string? gas = pipe.Gas ?? (_pipeGasCache.TryGetValue(pipeId, out var cached) ? cached : null);
                Color color = gas != null && GasColors.TryGetValue(gas, out var gc) ? gc : VacuumTheme.GlowBlue;
                inputColors.Add(color);

                if (lastGas != null && !string.Equals(gas, lastGas, StringComparison.OrdinalIgnoreCase))
                    multipleGases = true;
                lastGas = gas;
            }

            if (!multipleGases || inputColors.Count < 2) continue;

            int r = 0, g = 0, b = 0;
            foreach (var c in inputColors) { r += c.R; g += c.G; b += c.B; }
            var blended = Color.FromArgb(r / inputColors.Count, g / inputColors.Count, b / inputColors.Count);

            foreach (var pipe in items)
            {
                string from = pipe.From ?? "";
                string pipeId = pipe.Id ?? "";
                if (!from.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (_pipeStates.TryGetValue(pipeId, out var state) && state == PipeState.Flowing)
                {
                    _pipeColorOverrides[pipeId] = blended;
                    PropagateColorOverride(pipe, blended);
                }
            }
        }
    }

    private void PropagateColorOverride(XML_Pipe sourcePipe, Color blended,
        HashSet<string>? visited = null)
    {
        visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string targetId = (sourcePipe.To ?? "").Split(':')[0];
        if (!visited.Add(targetId)) return;
        if (!_passiveComponents.Contains(targetId) && !_openComponents.Contains(targetId)) return;
        if (_mixerComponents.Contains(targetId)) return;

        var items = Schematic?.Pipes?.Items;
        if (items == null) return;

        string prefix = targetId + ":";
        foreach (var pipe in items)
        {
            string from = pipe.From ?? "";
            string pipeId = pipe.Id ?? "";
            if (!from.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!(_pipeStates.TryGetValue(pipeId, out var state) && state == PipeState.Flowing)) continue;
            _pipeColorOverrides[pipeId] = blended;
            PropagateColorOverride(pipe, blended, visited);
        }
    }

    private string? DeriveGas(string fromSpec)
    {
        var parts = fromSpec.Split(':');
        if (parts.Length != 2) return null;
        string componentId = parts[0];
        foreach (Control control in Controls)
        {
            if (control is MFCControl mfc &&
                string.Equals(mfc.ID, componentId, StringComparison.OrdinalIgnoreCase))
                return mfc.Gas;
        }
        return null;
    }

    /// <summary>
    /// Positions are on the outside surface of the wall.
    /// </summary>
    private static List<ChamberAnchorDef> GenerateChamberAnchors(XML_Chamber c)
    {
        var anchors = new List<ChamberAnchorDef>();

        for (int i = 1; i <= c.TopAnchors; i++)
        {
            float x = c.Left + c.Width * i / (c.TopAnchors + 1);
            anchors.Add(new ChamberAnchorDef($"top_{i}", new PointF(x, c.Top)));
        }
        for (int i = 1; i <= c.BottomAnchors; i++)
        {
            float x = c.Left + c.Width * i / (c.BottomAnchors + 1);
            anchors.Add(new ChamberAnchorDef($"bottom_{i}", new PointF(x, c.Top + c.Height)));
        }
        for (int i = 1; i <= c.LeftAnchors; i++)
        {
            float y = c.Top + c.Height * i / (c.LeftAnchors + 1);
            anchors.Add(new ChamberAnchorDef($"left_{i}", new PointF(c.Left, y)));
        }
        for (int i = 1; i <= c.RightAnchors; i++)
        {
            float y = c.Top + c.Height * i / (c.RightAnchors + 1);
            anchors.Add(new ChamberAnchorDef($"right_{i}", new PointF(c.Left + c.Width, y)));
        }

        return anchors;
    }

    // Test seams for CoreLogicTests (InternalsVisibleTo).

    internal void AddTestChamberAnchor(string chamberId, string anchorName, PointF percentPos)
    {
        var existing = _chambers.FirstOrDefault(c =>
            string.Equals(c.Id, chamberId, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Anchors.Add(new ChamberAnchorDef(anchorName, percentPos));
            return;
        }
        var chamber = new ChamberDef(chamberId, null, 0f, 0f, 0f, 0f, 0f,
            new List<ChamberAnchorDef> { new(anchorName, percentPos) });
        _chambers.Add(chamber);
    }

    internal void AddTestDeviceWithName(string winFormsName)
    {
        var ctrl = new System.Windows.Forms.Label
        {
            Name = winFormsName,
            Location = new System.Drawing.Point(900, 900),
            Size = new System.Drawing.Size(10, 10),
        };
        Controls.Add(ctrl);
    }

    internal void SetTestCanvasArea(Rectangle area)
    {
        // CanvasArea has a private setter on the base — accessible via reflection.
        var prop = typeof(APS.Controls.Vacuum.Schematic.VacuumSchematicCanvas)
            .GetProperty(nameof(CanvasArea),
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        prop!.SetValue(this, area);
    }

    internal PointF? ResolveEndpointForTest(string spec) => ResolveEndpoint(spec);
}
