using System.Windows.Forms;
using APS.Controls.Theme;

namespace APS.SystemControl.Forms;

partial class FrmHwDiagnostics
{
    private System.ComponentModel.IContainer components = null;
    private DataGridView grid;
    private Button btnPing;
    private Label lblBanner;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { components?.Dispose(); }
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        Text = "Hardware Diagnostics";
        Width = DpiScale.Px(820);
        Height = DpiScale.Px(480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        lblBanner = new Label
        {
            Dock = DockStyle.Top,
            Height = DpiScale.Px(28),
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Visible = false,
            Padding = new Padding(DpiScale.Px(8), 0, 0, 0),
        };

        grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            ReadOnly = true,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
        };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Device",   HeaderText = "Device",   Width = DpiScale.Px(120) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type",     HeaderText = "Type",     Width = DpiScale.Px(90)  });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Result",   HeaderText = "Result",   Width = DpiScale.Px(110) });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ms",       HeaderText = "ms",       Width = DpiScale.Px(60)  });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Identity", HeaderText = "Identity", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Detail",   HeaderText = "Detail",   Width = DpiScale.Px(200) });

        btnPing = new Button
        {
            Text = "Ping Hardware",
            Dock = DockStyle.Bottom,
            Height = DpiScale.Px(36),
        };

        Controls.Add(grid);
        Controls.Add(btnPing);
        Controls.Add(lblBanner);
    }
}
