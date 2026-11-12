using System.Reflection.PortableExecutable;
using APS.Controls.General;
using APS.SystemControl.Controls.General;
using APS.SystemControl.Core;

namespace APS.Controls.Generic;

public class OperationsPanel : ScalablePanel, IBaseCustomPanel
{
    private GroupBox gbLoadlock = new GroupBox();
    private GroupBox gbSystem = new GroupBox();
    private GroupBox gbSubstrate = new GroupBox();
    private Button btnAirRelease = new Button();
    private Button btnLLPumpOut = new Button();
    private Button btnLoadSubstrate = new Button();
    private Button btnUnloadSubstrate = new Button();
    private Button btnSysAirRelease = new Button();
    private Button btnSysShutdown = new Button();
    private Button btnSysPumpOut = new Button();
    private Button btnSysStart = new Button();

    public OperationsPanel()
    {
        InitializePanel();
        if (IsDark) ApplyDarkButtons();
        ScaleControls();
    }

    private void ApplyDarkButtons()
    {
        var darkBg = Color.FromArgb(27, 40, 56);
        var darkBorder = Color.FromArgb(34, 51, 68);
        var lightText = Color.FromArgb(200, 214, 229);

        foreach (var gb in new[] { gbSubstrate, gbSystem, gbLoadlock })
        {
            gb.ForeColor = lightText;
            foreach (var btn in gb.Controls.OfType<Button>())
            {
                btn.FlatStyle = FlatStyle.Flat;
                btn.BackColor = darkBg;
                btn.ForeColor = lightText;
                btn.FlatAppearance.BorderColor = darkBorder;
                btn.FlatAppearance.BorderSize = 1;
            }
        }
    }

    public event EventHandler<CustomEventArgs>? ButtonClickEvent;

    public string ID { get; set; } = "System";
    public DeviceType DeviceType => DeviceType.System;

    new public string Name { get => base.Name; }

    public void Update(object[] args)
    {

    }

    private void Button_Click(object? sender, EventArgs e)
    {
        if (sender is Button button && button.Tag != null)
        {
            ButtonClickEvent?.Invoke(this, new CustomEventArgs(new object[] { button.Tag }));
        }
    }

    private bool IsDark => APS.Controls.Theme.VacuumTheme.IsDark;

    private void InitializePanel()
    {
        this.Size = new Size(200, 230 + HeaderHeight);
        this.BackColor = IsDark ? Color.FromArgb(13, 27, 42) : SystemColors.ButtonFace;
        this.BorderStyle = BorderStyle.FixedSingle;
        this.Controls.Add(gbLoadlock);
        this.Controls.Add(gbSystem);
        this.Controls.Add(gbSubstrate);
        this.Location = new Point(5, 5);
        this.Padding = new Padding((int)(5 * Dpi), HeaderHeightPx + (int)(5 * Dpi), (int)(5 * Dpi), (int)(5 * Dpi));
        this.TabIndex = 8;

        gbLoadlock.Controls.Add(btnAirRelease);
        gbLoadlock.Controls.Add(btnLLPumpOut);
        gbLoadlock.Dock = DockStyle.Top;
        gbLoadlock.Location = new Point(5, 147);
        gbLoadlock.Name = "gbLoadlock";
        gbLoadlock.Size = new Size(176, 57);
        gbLoadlock.TabIndex = 2;
        gbLoadlock.TabStop = false;
        gbLoadlock.Text = "LoadLock";
        btnAirRelease.Location = new Point(92, 22);
        btnAirRelease.Name = "btnAirRelease";
        btnAirRelease.Size = new Size(80, 23);
        btnAirRelease.TabIndex = 2;
        btnAirRelease.Tag = "LOADLOCK_AIR_RELEASE";
        btnAirRelease.Text = "Air Release";
        btnAirRelease.UseVisualStyleBackColor = false;
        btnAirRelease.Click += Button_Click;

        btnLLPumpOut.Location = new Point(6, 22);
        btnLLPumpOut.Name = "btnLLPumpOut";
        btnLLPumpOut.Size = new Size(80, 23);
        btnLLPumpOut.TabIndex = 1;
        btnLLPumpOut.Tag = "LOADLOCK_PUMP_OUT";
        btnLLPumpOut.Text = "Pump out";
        btnLLPumpOut.UseVisualStyleBackColor = false;
        btnLLPumpOut.Click += Button_Click;

        gbSystem.Controls.Add(btnSysAirRelease);
        gbSystem.Controls.Add(btnSysShutdown);
        gbSystem.Controls.Add(btnSysPumpOut);
        gbSystem.Controls.Add(btnSysStart);
        gbSystem.Dock = DockStyle.Top;
        gbSystem.Location = new Point(5, 63);
        gbSystem.Name = "gbSystem";
        gbSystem.Size = new Size(176, 84);
        gbSystem.TabIndex = 1;
        gbSystem.TabStop = false;
        gbSystem.Text = "System";
        btnSysAirRelease.Location = new Point(92, 51);
        btnSysAirRelease.Name = "btnSysAirRelease";
        btnSysAirRelease.Size = new Size(80, 23);
        btnSysAirRelease.TabIndex = 4;
        btnSysAirRelease.Tag = "SYSTEM_AIR_RELEASE";
        btnSysAirRelease.Text = "Air release";
        btnSysAirRelease.UseVisualStyleBackColor = false;
        btnSysAirRelease.Click += Button_Click;

        btnSysShutdown.Location = new Point(6, 51);
        btnSysShutdown.Name = "btnSysShutdown";
        btnSysShutdown.Size = new Size(80, 23);
        btnSysShutdown.TabIndex = 3;
        btnSysShutdown.Tag = "SYSTEM_SHUTDOWN";
        btnSysShutdown.Text = "Shutdown";
        btnSysShutdown.UseVisualStyleBackColor = false;
        btnSysShutdown.Click += Button_Click;
        btnSysPumpOut.Location = new Point(92, 22);
        btnSysPumpOut.Name = "btnSysPumpOut";
        btnSysPumpOut.Size = new Size(80, 23);
        btnSysPumpOut.TabIndex = 2;
        btnSysPumpOut.Tag = "SYSTEM_PUMP_OUT";
        btnSysPumpOut.Text = "Pump Out";
        btnSysPumpOut.UseVisualStyleBackColor = false;
        btnSysPumpOut.Click += Button_Click;
        btnSysStart.Location = new Point(6, 22);
        btnSysStart.Name = "btnSysStart";
        btnSysStart.Size = new Size(80, 23);
        btnSysStart.TabIndex = 1;
        btnSysStart.Tag = "SYSTEM_START";
        btnSysStart.Text = "Start";
        btnSysStart.UseVisualStyleBackColor = false;
        btnSysStart.Click += Button_Click;
        gbSubstrate.Controls.Add(btnUnloadSubstrate);
        gbSubstrate.Controls.Add(btnLoadSubstrate);
        gbSubstrate.Dock = DockStyle.Top;
        gbSubstrate.Location = new Point(5, 5);
        gbSubstrate.Name = "gbSubstrate";
        gbSubstrate.Size = new Size(176, 58);
        gbSubstrate.TabIndex = 0;
        gbSubstrate.TabStop = false;
        gbSubstrate.Text = "Substrate";
        btnUnloadSubstrate.Location = new Point(92, 22);
        btnUnloadSubstrate.Name = "btnUnloadSubstrate";
        btnUnloadSubstrate.Size = new Size(80, 23);
        btnUnloadSubstrate.TabIndex = 1;
        btnUnloadSubstrate.Tag = "UNLOAD_SUBSTRATE";
        btnUnloadSubstrate.Text = "Unload";
        btnUnloadSubstrate.UseVisualStyleBackColor = false;
        btnUnloadSubstrate.Click += Button_Click;
        btnLoadSubstrate.Location = new Point(6, 22);
        btnLoadSubstrate.Name = "btnLoadSubstrate";
        btnLoadSubstrate.Size = new Size(80, 23);
        btnLoadSubstrate.TabIndex = 0;
        btnLoadSubstrate.Tag = "LOAD_SUBSTRATE";
        btnLoadSubstrate.Text = "Load";
        btnLoadSubstrate.UseVisualStyleBackColor = false;
        btnLoadSubstrate.Click += Button_Click;
    }
}
