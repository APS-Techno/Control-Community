using APS.Controls.Theme;
using APS.SystemControl.Controls.Theme;

namespace APS.SystemControl.Forms;

public class frmListEditor : Form
{
    private readonly ListBox _list;
    private readonly TextBox _txtNew;
    private readonly Button _btnAdd;
    private readonly Button _btnRemove;
    private HintManager _hints = null!;

    public List<string> Items { get; private set; }

    public frmListEditor(string title, IEnumerable<string> items)
    {
        Items = new List<string>(items);

        APS.SystemControl.Core.UIScaling.ApplyToForm(this);
        Text = title;
        Size = new Size(DpiScale.Px(350), DpiScale.Px(380));
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = VacuumTheme.PanelBackground;
        ForeColor = VacuumTheme.TextPrimary;

        _list = new ListBox
        {
            Location = new Point(DpiScale.Px(12), DpiScale.Px(12)),
            Size = new Size(DpiScale.Px(310), DpiScale.Px(240)),
            Font = ScaledFont.Create(10f),
            BackColor = VacuumTheme.PanelField,
            ForeColor = VacuumTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };
        foreach (var item in Items)
            _list.Items.Add(item);

        _txtNew = new TextBox
        {
            Location = new Point(DpiScale.Px(12), DpiScale.Px(262)),
            Size = new Size(DpiScale.Px(210), DpiScale.Px(24)),
            Font = ScaledFont.Create(10f),
            BackColor = VacuumTheme.PanelField,
            ForeColor = VacuumTheme.TextPrimary,
            BorderStyle = BorderStyle.FixedSingle
        };

        _btnAdd = new Button
        {
            Text = "Add", Location = new Point(DpiScale.Px(232), DpiScale.Px(260)), Size = new Size(DpiScale.Px(90), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = VacuumTheme.TextPrimary,
            BackColor = VacuumTheme.PanelField
        };
        _btnAdd.FlatAppearance.BorderColor = VacuumTheme.PanelBorder;
        _btnAdd.Click += (_, _) =>
        {
            string text = _txtNew.Text.Trim();
            if (string.IsNullOrEmpty(text) || Items.Contains(text)) return;
            Items.Add(text);
            _list.Items.Add(text);
            _txtNew.Clear();
        };

        _btnRemove = new Button
        {
            Text = "Remove", Location = new Point(DpiScale.Px(12), DpiScale.Px(296)), Size = new Size(DpiScale.Px(90), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = VacuumTheme.TextPrimary,
            BackColor = VacuumTheme.PanelField
        };
        _btnRemove.FlatAppearance.BorderColor = VacuumTheme.PanelBorder;
        _btnRemove.Click += (_, _) =>
        {
            if (_list.SelectedIndex < 0) return;
            int idx = _list.SelectedIndex;
            Items.RemoveAt(idx);
            _list.Items.RemoveAt(idx);
        };

        var btnOK = new Button
        {
            Text = "OK", Location = new Point(DpiScale.Px(142), DpiScale.Px(296)), Size = new Size(DpiScale.Px(80), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = VacuumTheme.TextPrimary,
            BackColor = VacuumTheme.PanelField,
            DialogResult = DialogResult.OK
        };
        btnOK.FlatAppearance.BorderColor = VacuumTheme.PanelBorder;

        var btnCancel = new Button
        {
            Text = "Cancel", Location = new Point(DpiScale.Px(232), DpiScale.Px(296)), Size = new Size(DpiScale.Px(90), DpiScale.Px(28)),
            FlatStyle = FlatStyle.Flat, ForeColor = VacuumTheme.TextPrimary,
            BackColor = VacuumTheme.PanelField,
            DialogResult = DialogResult.Cancel
        };
        btnCancel.FlatAppearance.BorderColor = VacuumTheme.PanelBorder;

        AcceptButton = btnOK;
        CancelButton = btnCancel;

        Controls.AddRange([_list, _txtNew, _btnAdd, _btnRemove, btnOK, btnCancel]);
        DarkModeHelper.FixControls(this);

        _hints = new HintManager(this);
        _hints.Set(_txtNew,    "Type a new item name here, then click Add.");
        _hints.Set(_btnAdd,    "Add the typed text as a new list entry (duplicates are ignored).");
        _hints.Set(_btnRemove, "Remove the selected item from the list.");

        // Force dark title bar (DarkModeHelper only does this in system dark mode)
        HandleCreated += (_, _) =>
        {
            // Derive the title-bar COLORREF from the palette so Palette.xml
            // PanelBackground overrides apply here too (default #0D1B2A).
            var bg = VacuumTheme.PanelBackground;
            int colorRef = bg.R | (bg.G << 8) | (bg.B << 16);
            DwmSetWindowAttribute(Handle, 35, ref colorRef, sizeof(int));
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _hints?.Dispose();
        base.Dispose(disposing);
    }

    [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
