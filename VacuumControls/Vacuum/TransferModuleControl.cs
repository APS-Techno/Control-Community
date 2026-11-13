using System.ComponentModel;
using APS.Controls.Core;
using APS.Controls.Dialogs;
using APS.Controls.Theme;

namespace APS.Controls.Vacuum;

/// <summary>
/// Up to 6 configured faces shown as labels around the hex. Slit-valve state lives in
/// the standalone GateValveControl widgets; pressure in the unified TM_pm LEDPanel indicator.
/// </summary>
[ToolboxItem(false)]
public class TransferModuleControl : ThemedVacuumControl
{
    /// <summary>
    /// Square reference size (px) at BaseScale = 1; scaled by the canvas.
    /// 240 (vs ChamberControl's 200) leaves clearance for face labels and arm extension.
    /// </summary>
    private const int ReferenceSize = 240;

    /// <summary>
    /// Hex circumradius as a fraction of the smaller widget dimension. 0.42 fills most of
    /// the box while leaving ~18 px outside each edge for the face-name labels.
    /// </summary>
    private const float HexRadiusFraction = 0.42f;

    private readonly string _id;
    private string _chamberId = "TM";
    private string _stateText = "—";
    private float _scale = 1.0f;

    private RobotState     _state     = RobotState.Startup;
    private RobotPhase     _phase     = RobotPhase.None;
    private byte           _currentFace;
    private bool           _hasWafer;
    private RobotErrorCode _errorCode = RobotErrorCode.Ok;

    private float _pressure;   // Torr
    private bool  _turboOn;
    private bool  _scrollOn;

    private IReadOnlyList<TmFace> _faces = Array.Empty<TmFace>();
    private readonly ToolTip _toolTip = new() { InitialDelay = 400, ReshowDelay = 200, AutoPopDelay = 30000 };

    // 0..255 normalised values from the PLC status bytes. 0 means "not supplied"
    // for AngleNorm (falls back to face-snap); ExtendNorm=0 means fully retracted.
    private byte _angleNorm;
    private byte _extendNorm;

    /// <summary>FaceIdx is 1..6.</summary>
    public sealed record TmFace(string Name, int FaceIdx, bool SlitOpen);

    public TransferModuleControl(string id)
    {
        _id = id;
        ID = id;
        Name = GUIConst.ControlName(DeviceType.Robot, id);
        DoubleBuffered = true;
        CalculateSize();
        // The hexagon does not fill its square bounds. Make the background transparent so
        // the schematic canvas (and the pipes routed to the hex-edge anchors) shows through
        // the non-hex regions instead of being covered by an opaque box. The hex body and
        // cavity are explicitly painted in DrawHexBody, so they remain opaque.
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    /// <summary>Chamber whose devices (pressure gauge, pumps) this widget mirrors.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string ChamberId
    {
        get => _chamberId;
        set { _chamberId = value; Invalidate(); }
    }

    /// <summary>
    /// Retained for host back-compat; not rendered
    /// (state is shown via the colour dot, which reads <see cref="State"/>).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string StateText
    {
        get => _stateText;
        set => _stateText = value;
    }

    /// <summary>
    /// Torr. Retained for host back-compat; not rendered inside the hex — TM pressure is
    /// shown by the unified TM_pm LEDPanel indicator, the same readout LL and C1 use.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Pressure
    {
        get => _pressure;
        set => _pressure = value;
    }

    /// <summary>
    /// Retained for host back-compat; not rendered (the turbo pump is a separate schematic widget).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool TurboOn
    {
        get => _turboOn;
        set => _turboOn = value;
    }

    /// <summary>
    /// Retained for host back-compat; not rendered (the scroll pump is a separate schematic widget).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ScrollOn
    {
        get => _scrollOn;
        set => _scrollOn = value;
    }

    /// <summary>Setting to null is treated as empty; repaints automatically.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<TmFace> Faces
    {
        get => _faces;
        set { _faces = value ?? Array.Empty<TmFace>(); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RobotState State
    {
        get => _state;
        set { if (_state != value) { _state = value; UpdateToolTip(); Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RobotPhase Phase
    {
        get => _phase;
        set { if (_phase != value) { _phase = value; UpdateToolTip(); Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public byte CurrentFace
    {
        get => _currentFace;
        set { if (_currentFace != value) { _currentFace = value; Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool HasWafer
    {
        get => _hasWafer;
        set { if (_hasWafer != value) { _hasWafer = value; Invalidate(); } }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public RobotErrorCode ErrorCode
    {
        get => _errorCode;
        set { if (_errorCode != value) { _errorCode = value; Invalidate(); } }
    }

    /// <summary>
    /// Arm rotation as a PLC status byte (0..255 = 0..360°).
    /// When non-zero the animation uses this value directly.
    /// When 0 the widget falls back to a discrete face-snap from <see cref="CurrentFace"/>.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public byte AngleNorm
    {
        get => _angleNorm;
        set { _angleNorm = value; Invalidate(); }
    }

    /// <summary>
    /// Arm extension as a PLC status byte (0..255 = fully retracted..fully extended).
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public byte ExtendNorm
    {
        get => _extendNorm;
        set { _extendNorm = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        get => _scale;
        set
        {
            if (!float.IsFinite(value) || value <= 0f) return;
            _scale = value;
            CalculateSize();
            Invalidate();
        }
    }

    protected override void CalculateSize()
    {
        int s = Math.Max(48, (int)Math.Round(ReferenceSize * BaseScale * _scale));
        Size = new Size(s, s);
    }

    private static float HexRadius(int w, int h) => Math.Min(w, h) * HexRadiusFraction;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        if (w < 48 || h < 48) return;

        var centre = new PointF(w / 2f, h / 2f);
        float radius = HexRadius(w, h);

        DrawHexBody(g, centre, radius);
        DrawFaces(g, centre, radius);
        DrawArm(g, centre, radius);

        // TM pressure is intentionally NOT drawn here: it is shown by the unified TM_pm
        // LEDPanel indicator (same readout LL/C1 use), so an in-hex LCD would duplicate
        // it and look inconsistent with the other chambers.

        DrawStateDot(g, _state, centre, radius);
        DrawIdLabel(g, centre, radius);
        DrawAnchorOverlay(g);
    }

    private static void DrawHexBody(Graphics g, PointF centre, float radius)
    {
        var verts = TransferModuleHexGeometry.Vertices(centre, radius);

        // Metal-gradient fill clipped to the hex outline. The flat-top hex spans 2r
        // wide but only √3·r tall, so use a tight box (not a 2r square) for the gradient.
        const float halfHeightFactor = 0.8660254f; // √3/2
        float halfH = radius * halfHeightFactor;
        var bounds = Rectangle.Round(new RectangleF(
            centre.X - radius, centre.Y - halfH, radius * 2f, halfH * 2f));
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            using var metal = VacuumTheme.CreateMetalGradient(bounds);
            g.FillPolygon(metal, verts);
        }
        using (var edge = new Pen(VacuumTheme.MetalEdge, 1.4f))
            g.DrawPolygon(edge, verts);

        var innerVerts = TransferModuleHexGeometry.Vertices(centre, radius * 0.82f);
        using (var cavity = new SolidBrush(VacuumTheme.CavityDark))
            g.FillPolygon(cavity, innerVerts);
        using (var innerPen = new Pen(Color.FromArgb(100, VacuumTheme.MetalDark), 0.8f))
            g.DrawPolygon(innerPen, innerVerts);
    }

    /// <summary>
    /// Positioned relative to the hex centre/radius so it tracks the vessel; the state
    /// name is shown via the hover tooltip.
    /// </summary>
    private void DrawStateDot(Graphics g, RobotState state, PointF centre, float radius)
    {
        Color accent = state switch
        {
            RobotState.Idle                              => Color.LimeGreen,
            RobotState.Homing or RobotState.Transferring => Color.DodgerBlue,
            RobotState.Error                             => Color.OrangeRed,
            _   /* NotHomed, Startup */                  => Color.Gold,
        };

        const float d = 10f;
        float x = centre.X - d / 2f;
        float y = centre.Y - radius * 0.65f;
        using var fill = new SolidBrush(accent);
        using var ring = new Pen(VacuumTheme.MetalDark, 1.2f);
        g.FillEllipse(fill, x, y, d, d);
        g.DrawEllipse(ring, x, y, d, d);
    }

    /// <summary>
    /// The slit-valve open/closed state is shown by the standalone GateValveControl
    /// widgets wired to these faces in Controls.xml, not duplicated as a glyph here.
    /// </summary>
    private void DrawFaces(Graphics g, PointF centre, float radius)
    {
        using var faceFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var faceLabelBrush = new SolidBrush(VacuumTheme.TextPrimary);

        foreach (var face in _faces)
        {
            if (face.FaceIdx < 1 || face.FaceIdx > 6) continue;

            var mid = TransferModuleHexGeometry.FaceMidpoint(centre, radius, face.FaceIdx);

            float dx = mid.X - centre.X;
            float dy = mid.Y - centre.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) continue;
            float ux = dx / len;
            float uy = dy / len;

            var labelPos = new PointF(mid.X + ux * 18f, mid.Y + uy * 18f);
            var lblSize  = g.MeasureString(face.Name, faceFont);
            g.DrawString(face.Name, faceFont, faceLabelBrush,
                labelPos.X - lblSize.Width  / 2f,
                labelPos.Y - lblSize.Height / 2f);
        }
    }

    /// <summary>
    /// Returns an empty string for <see cref="RobotPhase.None"/> or unrecognised values.
    /// </summary>
    private static string PhaseToLabel(RobotPhase phase, byte currentFace, IReadOnlyList<TmFace> faces)
    {
        string faceName = faces.FirstOrDefault(f => f.FaceIdx == currentFace)?.Name ?? "?";
        return phase switch
        {
            RobotPhase.OpenSrcSlit    => $"opening slit at {faceName}",
            RobotPhase.RotateToSrc    => $"rotating to {faceName}",
            RobotPhase.ExtendToSrc    => $"extending into {faceName}",
            RobotPhase.LiftAtSrc      => $"picking at {faceName}",
            RobotPhase.RetractFromSrc => $"retracting from {faceName}",
            RobotPhase.CloseSrcSlit   => $"closing slit at {faceName}",
            RobotPhase.RotateToDst    => $"rotating to {faceName}",
            RobotPhase.OpenDstSlit    => $"opening slit at {faceName}",
            RobotPhase.ExtendToDst    => $"extending into {faceName}",
            RobotPhase.LowerAtDst     => $"placing at {faceName}",
            RobotPhase.RetractFromDst => $"retracting from {faceName}",
            RobotPhase.CloseDstSlit   => $"closing slit at {faceName}",
            _                         => "",
        };
    }

    /// <summary>No-op until the handle exists (keeps headless tests safe).</summary>
    private void UpdateToolTip()
    {
        if (!IsHandleCreated) return;
        string phase = _state == RobotState.Transferring
            ? PhaseToLabel(_phase, _currentFace, _faces)
            : "";
        string txt = phase.Length > 0 ? $"{_id}: {_state} — {phase}" : $"{_id}: {_state}";
        _toolTip.SetToolTip(this, txt);
    }

    /// <summary>
    /// The flat-top hex leaves no room above its top edge once it fills the square frame
    /// (the top face name label sits there), so the ID is drawn inside the vessel like a
    /// chamber label.
    /// </summary>
    private void DrawIdLabel(Graphics g, PointF centre, float radius)
    {
        using var idFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var idBrush = new SolidBrush(VacuumTheme.TextDim);
        var idSize = g.MeasureString(_id, idFont);
        g.DrawString(_id, idFont, idBrush,
            centre.X - idSize.Width / 2f,
            centre.Y - radius * 0.5f);
    }

    /// <summary>
    /// Returns the arm rotation angle in degrees (0° = top, increasing CW).
    /// If <see cref="AngleNorm"/> is non-zero it is used directly as a 0–360° fraction;
    /// otherwise the angle is snapped to the midpoint of <see cref="CurrentFace"/> (1–6).
    /// Defaults to 180° (south / park) when no face is selected.
    /// </summary>
    private float EffectiveAngleDeg()
    {
        if (_angleNorm != 0)
            return _angleNorm * 360f / 255f;
        if (_currentFace >= 1 && _currentFace <= 6)
            return (_currentFace - 1) * 60f;
        return 180f; // park — pointing south between face 3 and face 4
    }

    private float EffectiveExtendNorm() => _extendNorm / 255f;

    private void DrawArm(Graphics g, PointF centre, float radius)
    {
        float armRetracted = radius * 0.35f;
        var tip = TransferModuleHexGeometry.ArmTip(
            centre, radius, armRetracted,
            EffectiveAngleDeg(), EffectiveExtendNorm());

        using var armPen    = new Pen(VacuumTheme.MetalDark, 4f);
        using var armOutline = new Pen(VacuumTheme.GlowBlue, 1f);
        g.DrawLine(armPen,    centre, tip);
        g.DrawLine(armOutline, centre, tip);

        using var eeBrush = new SolidBrush(VacuumTheme.MetalDark);
        using var eePen   = new Pen(VacuumTheme.GlowBlue, 1f);
        var eeRect = new RectangleF(tip.X - 8f, tip.Y - 5f, 16f, 10f);
        g.FillRectangle(eeBrush, eeRect);
        g.DrawRectangle(eePen, eeRect.X, eeRect.Y, eeRect.Width, eeRect.Height);

        if (_hasWafer)
        {
            using var waferBrush = new SolidBrush(Color.Silver);
            using var waferPen   = new Pen(VacuumTheme.GlowBlue, 1f);
            g.FillEllipse(waferBrush, tip.X - 6f, tip.Y - 6f, 12f, 12f);
            g.DrawEllipse(waferPen,   tip.X - 6f, tip.Y - 6f, 12f, 12f);
        }
    }

    /// <summary>
    /// Six face-midpoint anchors (face1..face6, face1 = top, advancing 60° clockwise —
    /// matching the robot's 1..6 face indices) for slit/chamber pipes, plus six vertex
    /// anchors (vertex1..vertex6) at the corners for the TM's own pump/roughing lines.
    /// Vertices run clockwise like the faces: vertexN is the corner clockwise-adjacent to
    /// faceN (vertex1 = upper-right .. vertex6 = upper-left).
    /// </summary>
    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        var centre = new PointF(Width / 2f, Height / 2f);
        float r = HexRadius(Width, Height);

        var list = new List<AnchorDef>(12);
        for (int i = 1; i <= TransferModuleHexGeometry.FaceCount; i++)
        {
            var m = TransferModuleHexGeometry.FaceMidpoint(centre, r, i);
            list.Add(new AnchorDef($"face{i}", Point.Round(m)));
        }
        // Vertices() returns corners counter-clockwise from the upper-right; re-index so
        // vertexN runs clockwise like the faces.
        var verts = TransferModuleHexGeometry.Vertices(centre, r);
        for (int i = 1; i <= TransferModuleHexGeometry.FaceCount; i++)
            list.Add(new AnchorDef($"vertex{i}", Point.Round(verts[(7 - i) % 6])));
        return list;
    }

    /// <summary>
    /// Fired when the operator clicks <b>Home</b> in the service dialog.
    /// The host should forward to <c>BaseRobot.Home()</c>.
    /// </summary>
    public event EventHandler? HomeRequested;

    /// <summary>
    /// Fired when the operator clicks <b>Abort</b> in the service dialog.
    /// The host should forward to <c>BaseRobot.Abort()</c>.
    /// </summary>
    public event EventHandler? AbortRequested;

    /// <summary>
    /// Fired when the operator clicks <b>Transfer</b> in the service dialog.
    /// The tuple carries the source and destination face indices (0 = park).
    /// The host should forward to <c>BaseRobot.Transfer(src, dst)</c>.
    /// </summary>
    public event EventHandler<(byte src, byte dst)>? TransferRequested;

    /// <summary>Taken from <c>BaseRobot.Faces</c>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<(string Name, byte Idx)> ConfiguredFaces { get; set; }
        = Array.Empty<(string, byte)>();

    private void OpenServiceDialog()
    {
        string faceName = ConfiguredFaces
            .FirstOrDefault(f => f.Idx == _currentFace)
            .Name ?? "park";

        using var dlg = new TransferModuleServiceDialog(_id, ConfiguredFaces);
        dlg.UpdateMirror(_state, _phase, faceName, _hasWafer, _errorCode);

        if (dlg.ShowDialog(FindForm()) == DialogResult.OK)
        {
            switch (dlg.SelectedAction)
            {
                case TransferModuleServiceDialog.Action.Home:
                    HomeRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case TransferModuleServiceDialog.Action.Abort:
                    AbortRequested?.Invoke(this, EventArgs.Empty);
                    break;
                case TransferModuleServiceDialog.Action.Transfer:
                    TransferRequested?.Invoke(this, (dlg.SelectedSrc, dlg.SelectedDst));
                    break;
            }
        }
    }

    /// <summary>Right-click anywhere opens the service dialog.</summary>
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Right)
            OpenServiceDialog();
    }

    /// <summary>
    /// Populate the tooltip once the handle exists, so it is non-empty even if the
    /// robot state never changes from its initial value (UpdateToolTip no-ops before
    /// the handle is created, so the setter-driven updates may not have applied yet).
    /// </summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateToolTip();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
