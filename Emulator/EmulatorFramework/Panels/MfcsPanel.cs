using APS.Controls.Theme;
using APS.SystemEmulator.Engine;
using APS.Emulator.Devices.S7;

namespace APS.SystemEmulator.Panels;

internal sealed class MfcsPanel : UserControl
{
    private readonly GroupBox _group;
    private readonly TableLayoutPanel _grid;

    private SimulationEngine? _engine;
    private readonly List<MfcColumn> _columns = new();

    public int MfcColumnCount => _columns.Count;
    public bool HasContent => _columns.Count > 0;

    public MfcsPanel()
    {
        Dock = DockStyle.Fill;
        _grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(22)));
        _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(22)));
        _grid.RowStyles.Add(new RowStyle(SizeType.Absolute, DpiScale.Px(22)));

        _group = new GroupBox
        {
            Text = "MFCs (Gas)",
            Dock = DockStyle.Fill,
            Padding = new Padding(DpiScale.Px(4)),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
        };
        _group.Controls.Add(_grid);
        Controls.Add(_group);

        DarkModeHelper.FixControls(this);
    }

    public void AttachEngine(SimulationEngine engine)
    {
        _engine = engine;
        Rebuild();
    }

    public void UpdateValues()
    {
        if (_engine == null) return;
        foreach (var col in _columns)
        {
            col.Setpoint.Text = col.Mfc.Setpoint.ToString("F1");
            col.Actual.Text   = col.Mfc.ActualFlow.ToString("F1");
        }
    }

    private void Rebuild()
    {
        // Dispose existing controls to release GDI Font handles
        while (_grid.Controls.Count > 0)
        {
            var c = _grid.Controls[0];
            _grid.Controls.RemoveAt(0);
            c.Dispose();
        }
        _grid.ColumnStyles.Clear();
        _columns.Clear();
        _grid.ColumnCount = 1;

        if (_engine == null) return;

        var mfcs = _engine.Devices.OfType<MFCEmulator>().ToList();
        if (mfcs.Count == 0) return;

        _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, DpiScale.Px(80)));
        _grid.Controls.Add(MakeLabel("",          header: true), 0, 0);
        _grid.Controls.Add(MakeLabel("Setpoint:", header: true), 0, 1);
        _grid.Controls.Add(MakeLabel("Actual:",   header: true), 0, 2);

        _grid.ColumnCount = 1 + mfcs.Count;
        for (int i = 0; i < mfcs.Count; i++)
        {
            _grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / mfcs.Count));

            var nameLbl = MakeLabel(mfcs[i].Id, header: true,  ContentAlignment.MiddleLeft);
            var setLbl  = MakeLabel("0.0",      header: false, ContentAlignment.MiddleLeft);
            var actLbl  = MakeLabel("0.0",      header: false, ContentAlignment.MiddleLeft);

            _grid.Controls.Add(nameLbl, i + 1, 0);
            _grid.Controls.Add(setLbl,  i + 1, 1);
            _grid.Controls.Add(actLbl,  i + 1, 2);

            _columns.Add(new MfcColumn(mfcs[i], nameLbl, setLbl, actLbl));
        }

        UpdateValues();
    }

    private static Label MakeLabel(string text, bool header, ContentAlignment align = ContentAlignment.MiddleRight) => new()
    {
        Text = text,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = align,
        Font = new Font(header ? "Segoe UI" : "Consolas", header ? 9f : 10f, header ? FontStyle.Bold : FontStyle.Regular),
    };

    private sealed record MfcColumn(MFCEmulator Mfc, Label Name, Label Setpoint, Label Actual);
}
