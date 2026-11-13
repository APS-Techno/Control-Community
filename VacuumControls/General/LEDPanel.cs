using System.ComponentModel;
using System.Drawing.Drawing2D;
using APS.Controls.Theme;

namespace APS.Controls.General;

[ToolboxItem(false)]
public enum PanelStyle { Vert, Horiz };

public class LEDPanel : ThemedVacuumControl
{
    readonly private LEDDisplay ledDisplay;
    readonly private Label label;
    readonly private Label _unitsLabel;

    private readonly PanelStyle _style;
    private float _scale = 1.0f;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float VScale
    {
        set
        {
            _scale = value;
            CalculateSize();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FrameColor { get; set; } = VacuumTheme.IsDark
        ? VacuumTheme.GlowBlue
        : Color.DarkBlue;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Format { get; set; } = "0.##e-0";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FontColor { get => ledDisplay.ForeColor; set => ledDisplay.ForeColor = value; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Units
    {
        get => _unitsLabel.Text;
        set
        {
            _unitsLabel.Text = value;
            _unitsLabel.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public LEDPanel(string name, PanelStyle style = PanelStyle.Vert)
    {
        this.Name = name;
        _style = style;
        this.ledDisplay = new LEDDisplay();
        // SafeFontLabel (not Label): paint-guarded so a bad/disposed font on resize
        // can't white-X the caption/units or crash the process.
        this.label = new SafeFontLabel();
        this._unitsLabel = new SafeFontLabel();

        label.ForeColor = IsDark ? VacuumTheme.TextPrimary : SystemColors.ControlText;
        label.BackColor = Color.Transparent;

        _unitsLabel.ForeColor = IsDark ? VacuumTheme.TextDim : SystemColors.GrayText;
        _unitsLabel.BackColor = Color.Transparent;
        _unitsLabel.Visible = false;

        this.ledDisplay.Dock = DockStyle.Fill;
        this.ledDisplay.Margin = Padding.Empty;

        if (style == PanelStyle.Vert)
        {
            this.label.Dock = DockStyle.Bottom;
            this.label.TextAlign = ContentAlignment.MiddleCenter;

            _unitsLabel.Dock = DockStyle.Bottom;
            _unitsLabel.TextAlign = ContentAlignment.MiddleCenter;
        }
        else
        {
            this.label.Dock = DockStyle.Left;
            this.label.TextAlign = ContentAlignment.MiddleRight;
            label.AutoSize = true;

            _unitsLabel.Dock = DockStyle.Right;
            _unitsLabel.TextAlign = ContentAlignment.MiddleLeft;
            _unitsLabel.AutoSize = true;
            _unitsLabel.Padding = new Padding(2, 0, 2, 0);
        }
        // Fonts, bezel padding, and the vert units-label height are scale-derived
        // and assigned by UpdateScaledChrome (via CalculateSize below).

        // Dock order matters: highest index is processed first.
        // Add: ledDisplay(Fill), then unitsLabel, then label — so label
        // docks first (Bottom/Left), then unitsLabel (Bottom/Right), then Fill.
        this.Controls.Add(this.ledDisplay);
        this.Controls.Add(this._unitsLabel);
        this.Controls.Add(this.label);

        SetStyle(ControlStyles.Selectable, true);

        AttachDoubleClickEventHandlers(this);
        CalculateSize();
    }

    protected override IReadOnlyList<AnchorDef> DefineAnchors()
    {
        return [new("left", new Point(0, Height / 2))];
    }

    protected override void CalculateSize()
    {
        // Canvas-proportional, DPI-free on purpose: the schematic canvas is
        // measured in physical pixels, so a fixed fraction of it IS the right
        // size at any monitor DPI. A DeviceDpi term would make indicators outgrow
        // the chambers (which scale by BaseScale alone) on high-DPI monitors.
        float sf = BaseScale * _scale;
        if (_style == PanelStyle.Vert)
        {
            this.Width = (int)(80 * sf);
            this.Height = (int)(65 * sf);
        }
        else
        {
            this.Width = (int)(150 * sf);
            this.Height = (int)(35 * sf);
        }
        UpdateScaledChrome(sf);
    }

    private float _cachedChromeSf = -1f;

    // Fonts this panel created and OWNS; see SetOwnedFont.
    private Font? _ledFont, _ledLabelFont, _ledUnitsFont;

    /// <summary>
    /// PIXEL-unit fonts: point fonts would be DPI-multiplied by GDI+ on top of the
    /// canvas-proportional box math and outgrow the panel on high-DPI monitors.
    /// Pixel sizes are point sizes × 4/3, so 96-DPI rendering is unchanged at sf = 1.
    /// </summary>
    private void UpdateScaledChrome(float sf)
    {
        if (ledDisplay is null) return;   // base-ctor call-order guard
        if (Math.Abs(sf - _cachedChromeSf) < 0.001f) return;
        _cachedChromeSf = sf;

        int bezel = Math.Max(1, (int)Math.Round(3.5f * sf));
        this.Padding = new Padding(bezel);
        if (_style == PanelStyle.Vert)
            _unitsLabel.Height = Math.Max(8, (int)(14 * sf));

        SetOwnedFont(ledDisplay, ref _ledFont, new Font("Digital-7 mono",
            Math.Max(6f, LEDDisplay.DefaultFontSize * 4f / 3f * sf), FontStyle.Regular, GraphicsUnit.Pixel));
        SetOwnedFont(label, ref _ledLabelFont, new Font("Courier New",
            Math.Max(6f, 10f * 4f / 3f * sf), FontStyle.Bold, GraphicsUnit.Pixel));
        SetOwnedFont(_unitsLabel, ref _ledUnitsFont, new Font("Segoe UI",
            Math.Max(5.5f, 8.5f * 4f / 3f * sf), FontStyle.Regular, GraphicsUnit.Pixel));
    }

    private static void SetOwnedFont(Control c, ref Font? owned, Font next)
    {
        // Assign the NEW font before disposing, and dispose only the instance WE
        // created last — never c.Font, which may be the shared ambient/DefaultFont.
        // Control.Font ignores a value-equal font and keeps the old instance, which
        // happens whenever the size sits at its floor; disposing it would leave the
        // control painting with a disposed font.
        if (owned != null && next.Equals(owned))
        {
            next.Dispose();
            return;
        }
        c.Font = next;
        owned?.Dispose();
        owned = next;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ledFont?.Dispose();
            _ledLabelFont?.Dispose();
            _ledUnitsFont?.Dispose();
            _ledFont = _ledLabelFont = _ledUnitsFont = null;
        }
        base.Dispose(disposing);
    }

    private void AttachDoubleClickEventHandlers(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            child.DoubleClick += PanelDoubleClick!;

            if (child.HasChildren)
            {
                AttachDoubleClickEventHandlers(child);
            }
        }
    }

    public void UpdateState(float floatValue)
    {
        this.Value = float.IsPositiveInfinity(floatValue) ? "OVER"
            : float.IsNegativeInfinity(floatValue) ? "UNDER"
            : float.IsNaN(floatValue) ? "INVALID" : floatValue.ToString(Format);
        Refresh();
    }

    protected virtual void PanelDoubleClick(object sender, EventArgs e) { }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get { return this.label.Text; }
        set { this.label.Text = value; }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get { return this.ledDisplay.Text; }
        set { this.ledDisplay.Text = value; }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var bounds = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

        DrawMetalHousing(e.Graphics, bounds, 4);

        using var pen = new Pen(FrameColor, 1.5f);
        using var path = CreateRoundedRect(bounds, 4);
        e.Graphics.DrawPath(pen, path);
    }
}
