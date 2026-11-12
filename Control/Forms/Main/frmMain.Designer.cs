using APS.Controls.Theme;
using APS.Controls.Vacuum;
using APS.SystemControl.Controls.Vacuum;

namespace APS.SystemControl
{
    partial class FrmMain
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                // Dispose unmanaged resources if any
                if (disposing && (components != null))
                {
                    components.Dispose();
                }
                if (disposing) _hints?.Dispose();
                // Call the base class Dispose method
                base.Dispose(disposing);
                _disposed = true;
            }
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(FrmMain));
            statusStrip1 = new StatusStrip();
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            newRecepieToolStripMenuItem = new ToolStripMenuItem();
            openRecipieToolStripMenuItem = new ToolStripMenuItem();
            saveRecepieToolStripMenuItem = new ToolStripMenuItem();
            toolStripMenuItem1 = new ToolStripSeparator();
            exitToolStripMenuItem = new ToolStripMenuItem();
            recepieToolStripMenuItem = new ToolStripMenuItem();
            runToolStripMenuItem = new ToolStripMenuItem();
            rerunSeparatorToolStripMenuItem = new ToolStripSeparator();
            reRunFromSnapshotToolStripMenuItem = new ToolStripMenuItem();
            toolsToolStripMenuItem = new ToolStripMenuItem();
            serialConsoleToolStripMenuItem = new ToolStripMenuItem();
            serviceToolStripMenuItem = new ToolStripMenuItem();
            overridesToolStripMenuItem = new ToolStripMenuItem();
            isolateToolStripMenuItem = new ToolStripMenuItem();
            settingsToolStripMenuItem = new ToolStripMenuItem();
            hwDiagnosticsToolStripMenuItem = new ToolStripMenuItem();
            reloadGuiConfigToolStripMenuItem = new ToolStripMenuItem();
            hardwareToolStripMenuItem = new ToolStripMenuItem();
            helpToolStripMenuItem = new ToolStripMenuItem();
            userManualToolStripMenuItem = new ToolStripMenuItem();
            aboutToolStripMenuItem = new ToolStripMenuItem();
            toolStrip1 = new ToolStrip();
            btnSelect = new ToolStripButton();
            toolStripSeparator1 = new ToolStripSeparator();
            btnEdit = new ToolStripButton();
            toolStripSeparator2 = new ToolStripSeparator();
            btnStart = new ToolStripButton();
            toolStripButton1 = new ToolStripButton();
            toolStripProgressBar1 = new ToolStripProgressBar();
            lblRecepieFileName = new ToolStripLabel();
            toolStripLabel1 = new ToolStripLabel();
            tabControlMain = new DarkTabControl();
            tabSystem = new TabPage();
            vacuumSchematic = new VacuumSchematic();
            panelHwLeft = new Panel();
            panelHwRight = new Panel();
            tabGraphs = new TabPage();
            graphLayout = new TableLayoutPanel();
            tabProcess = new TabPage();
            tableLayoutPanel3 = new TableLayoutPanel();
            tabLog = new TabPage();
            logPanelLayout = new TableLayoutPanel();
            logGridView = new DataGridView();
            panel1 = new Panel();
            button9 = new Button();
            button6 = new Button();
            button5 = new Button();
            menuStrip1.SuspendLayout();
            toolStrip1.SuspendLayout();
            tabControlMain.SuspendLayout();
            tabSystem.SuspendLayout();
            tabGraphs.SuspendLayout();
            tabProcess.SuspendLayout();
            tabLog.SuspendLayout();
            logPanelLayout.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)logGridView).BeginInit();
            panel1.SuspendLayout();
            SuspendLayout();
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(32, 32);
            // Items are added at runtime in FrmMain.InitStateIndicator (one tile per chamber).
            statusStrip1.Location = new Point(3, 954);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(1833, 22);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            //
            // menuStrip1
            //
            menuStrip1.ImageScalingSize = new Size(32, 32);
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem, recepieToolStripMenuItem, toolsToolStripMenuItem, serviceToolStripMenuItem, hardwareToolStripMenuItem, helpToolStripMenuItem });
            menuStrip1.Location = new Point(3, 3);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(1833, 24);
            menuStrip1.TabIndex = 1;
            menuStrip1.Text = "menuStrip1";
            // 
            // fileToolStripMenuItem
            // 
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { newRecepieToolStripMenuItem, openRecipieToolStripMenuItem, saveRecepieToolStripMenuItem, toolStripMenuItem1, exitToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "File";
            // 
            // newRecepieToolStripMenuItem
            // 
            newRecepieToolStripMenuItem.Name = "newRecepieToolStripMenuItem";
            newRecepieToolStripMenuItem.Size = new Size(141, 22);
            newRecepieToolStripMenuItem.Text = "New recepie";
            // 
            // openRecipieToolStripMenuItem
            // 
            openRecipieToolStripMenuItem.Name = "openRecipieToolStripMenuItem";
            openRecipieToolStripMenuItem.Size = new Size(141, 22);
            openRecipieToolStripMenuItem.Text = "Open recipie";
            openRecipieToolStripMenuItem.Click += ButtonLoadRecipe_Click;
            // 
            // saveRecepieToolStripMenuItem
            // 
            saveRecepieToolStripMenuItem.Name = "saveRecepieToolStripMenuItem";
            saveRecepieToolStripMenuItem.Size = new Size(141, 22);
            saveRecepieToolStripMenuItem.Text = "Save recepie";
            // 
            // toolStripMenuItem1
            // 
            toolStripMenuItem1.Name = "toolStripMenuItem1";
            toolStripMenuItem1.Size = new Size(138, 6);
            // 
            // exitToolStripMenuItem
            // 
            exitToolStripMenuItem.Name = "exitToolStripMenuItem";
            exitToolStripMenuItem.Size = new Size(141, 22);
            exitToolStripMenuItem.Text = "Exit";
            exitToolStripMenuItem.Click += ExitToolStripMenuItem_Click;
            // 
            // recepieToolStripMenuItem
            // 
            recepieToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { runToolStripMenuItem, rerunSeparatorToolStripMenuItem, reRunFromSnapshotToolStripMenuItem });
            recepieToolStripMenuItem.Name = "recepieToolStripMenuItem";
            recepieToolStripMenuItem.Size = new Size(54, 20);
            recepieToolStripMenuItem.Text = "Recipe";
            // 
            // runToolStripMenuItem
            // 
            runToolStripMenuItem.Name = "runToolStripMenuItem";
            runToolStripMenuItem.ShortcutKeys = Keys.F5;
            runToolStripMenuItem.Size = new Size(114, 22);
            runToolStripMenuItem.Text = "Run";
            runToolStripMenuItem.Click += RunToolStripMenuItem_Click;
            //
            // rerunSeparatorToolStripMenuItem
            //
            rerunSeparatorToolStripMenuItem.Name = "rerunSeparatorToolStripMenuItem";
            rerunSeparatorToolStripMenuItem.Size = new Size(180, 6);
            //
            // reRunFromSnapshotToolStripMenuItem
            //
            reRunFromSnapshotToolStripMenuItem.Name = "reRunFromSnapshotToolStripMenuItem";
            reRunFromSnapshotToolStripMenuItem.Size = new Size(200, 22);
            reRunFromSnapshotToolStripMenuItem.Text = "Re-run from snapshot…";
            reRunFromSnapshotToolStripMenuItem.Click += ReRunFromSnapshot_Click;
            //
            // toolsToolStripMenuItem
            // 
            toolsToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { serialConsoleToolStripMenuItem });
            toolsToolStripMenuItem.Name = "toolsToolStripMenuItem";
            toolsToolStripMenuItem.Size = new Size(47, 20);
            toolsToolStripMenuItem.Text = "Tools";
            // 
            // serialConsoleToolStripMenuItem
            // 
            serialConsoleToolStripMenuItem.Name = "serialConsoleToolStripMenuItem";
            serialConsoleToolStripMenuItem.Size = new Size(146, 22);
            serialConsoleToolStripMenuItem.Text = "Serial console";
            serialConsoleToolStripMenuItem.Click += SerialConsoleToolStripMenuItem_Click;
            //
            // serviceToolStripMenuItem
            //
            serviceToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { overridesToolStripMenuItem, isolateToolStripMenuItem, settingsToolStripMenuItem, hwDiagnosticsToolStripMenuItem, reloadGuiConfigToolStripMenuItem });
            serviceToolStripMenuItem.Name = "serviceToolStripMenuItem";
            serviceToolStripMenuItem.Size = new Size(56, 20);
            serviceToolStripMenuItem.Text = "Service";
            //
            // overridesToolStripMenuItem
            //
            overridesToolStripMenuItem.Name = "overridesToolStripMenuItem";
            overridesToolStripMenuItem.Size = new Size(180, 22);
            overridesToolStripMenuItem.Text = "Overrides…";
            overridesToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.O;
            overridesToolStripMenuItem.Click += OverridesToolStripMenuItem_Click;
            //
            // isolateToolStripMenuItem
            //
            isolateToolStripMenuItem.Name = "isolateToolStripMenuItem";
            isolateToolStripMenuItem.Size = new Size(180, 22);
            isolateToolStripMenuItem.Text = "Isolate System";
            isolateToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Shift | Keys.I;
            isolateToolStripMenuItem.Click += IsolateToolStripMenuItem_Click;
            //
            // settingsToolStripMenuItem
            //
            settingsToolStripMenuItem.Name = "settingsToolStripMenuItem";
            settingsToolStripMenuItem.Size = new Size(180, 22);
            settingsToolStripMenuItem.Text = "Settings…";
            settingsToolStripMenuItem.ShortcutKeys = Keys.Control | Keys.Oemcomma;
            settingsToolStripMenuItem.ShortcutKeyDisplayString = "Ctrl+,";
            settingsToolStripMenuItem.Click += SettingsToolStripMenuItem_Click;
            //
            // hwDiagnosticsToolStripMenuItem
            //
            hwDiagnosticsToolStripMenuItem.Name = "hwDiagnosticsToolStripMenuItem";
            hwDiagnosticsToolStripMenuItem.Size = new Size(180, 22);
            hwDiagnosticsToolStripMenuItem.Text = "Hardware Diagnostics";
            hwDiagnosticsToolStripMenuItem.Click += HwDiagnosticsToolStripMenuItem_Click;
            //
            // reloadGuiConfigToolStripMenuItem
            //
            reloadGuiConfigToolStripMenuItem.Name = "reloadGuiConfigToolStripMenuItem";
            reloadGuiConfigToolStripMenuItem.Size = new Size(180, 22);
            reloadGuiConfigToolStripMenuItem.Text = "Reload GUI Config";
            reloadGuiConfigToolStripMenuItem.Click += ReloadGuiConfigToolStripMenuItem_Click;
            //
            // hardwareToolStripMenuItem
            //
            hardwareToolStripMenuItem.Name = "hardwareToolStripMenuItem";
            hardwareToolStripMenuItem.Size = new Size(70, 20);
            hardwareToolStripMenuItem.Text = "Hardware";
            //
            // helpToolStripMenuItem
            //
            helpToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { userManualToolStripMenuItem, aboutToolStripMenuItem });
            helpToolStripMenuItem.Name = "helpToolStripMenuItem";
            helpToolStripMenuItem.Size = new Size(44, 20);
            helpToolStripMenuItem.Text = "Help";
            // 
            // userManualToolStripMenuItem
            // 
            userManualToolStripMenuItem.Name = "userManualToolStripMenuItem";
            userManualToolStripMenuItem.Size = new Size(140, 22);
            userManualToolStripMenuItem.Text = "User manual";
            // 
            // aboutToolStripMenuItem
            // 
            aboutToolStripMenuItem.Name = "aboutToolStripMenuItem";
            aboutToolStripMenuItem.Size = new Size(140, 22);
            aboutToolStripMenuItem.Text = "About";
            aboutToolStripMenuItem.Click += AboutToolStripMenuItem_Click;
            //
            // toolStrip1
            // 
            toolStrip1.AutoSize = false;
            toolStrip1.ImageScalingSize = new Size(32, 32);
            toolStrip1.Items.AddRange(new ToolStripItem[] { btnSelect, toolStripSeparator1, btnEdit, toolStripSeparator2, btnStart, toolStripButton1, toolStripProgressBar1, lblRecepieFileName, toolStripLabel1 });
            toolStrip1.Location = new Point(3, 27);
            toolStrip1.Margin = new Padding(5);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(1833, 42);
            toolStrip1.TabIndex = 11;
            toolStrip1.Text = "toolStrip1";
            // 
            // btnSelect
            // 
            btnSelect.Image = (Image)resources.GetObject("btnSelect.Image");
            btnSelect.ImageTransparentColor = Color.Magenta;
            btnSelect.Name = "btnSelect";
            btnSelect.Size = new Size(112, 39);
            btnSelect.Text = "Select Recipe";
            // 
            // toolStripSeparator1
            // 
            toolStripSeparator1.Name = "toolStripSeparator1";
            toolStripSeparator1.Size = new Size(6, 42);
            // 
            // btnEdit
            // 
            btnEdit.Image = (Image)resources.GetObject("btnEdit.Image");
            btnEdit.ImageTransparentColor = Color.Magenta;
            btnEdit.Name = "btnEdit";
            btnEdit.Size = new Size(63, 39);
            btnEdit.Text = "Edit";
            btnEdit.Click += btnEdit_Click;
            // 
            // toolStripSeparator2
            // 
            toolStripSeparator2.Name = "toolStripSeparator2";
            toolStripSeparator2.Size = new Size(6, 42);
            // 
            // btnStart
            // 
            btnStart.DisplayStyle = ToolStripItemDisplayStyle.Image;
            btnStart.Image = (Image)resources.GetObject("btnStart.Image");
            btnStart.ImageTransparentColor = Color.Magenta;
            btnStart.Name = "btnStart";
            btnStart.Size = new Size(36, 39);
            btnStart.Text = "toolStripButton3";
            btnStart.Click += RunToolStripMenuItem_Click;
            // 
            // toolStripButton1
            // 
            toolStripButton1.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButton1.Image = (Image)resources.GetObject("toolStripButton1.Image");
            toolStripButton1.ImageTransparentColor = Color.Magenta;
            toolStripButton1.Name = "toolStripButton1";
            toolStripButton1.Size = new Size(36, 39);
            toolStripButton1.Text = "btnAbort";
            // 
            // toolStripProgressBar1
            // 
            toolStripProgressBar1.Alignment = ToolStripItemAlignment.Right;
            toolStripProgressBar1.AutoSize = false;
            toolStripProgressBar1.Margin = new Padding(3);
            toolStripProgressBar1.Name = "toolStripProgressBar1";
            toolStripProgressBar1.Overflow = ToolStripItemOverflow.Never;
            toolStripProgressBar1.Size = new Size(300, 25);
            // 
            // lblRecepieFileName
            // 
            lblRecepieFileName.Alignment = ToolStripItemAlignment.Right;
            lblRecepieFileName.Font = new Font("Segoe UI", 10F);
            lblRecepieFileName.Name = "lblRecepieFileName";
            lblRecepieFileName.Size = new Size(59, 39);
            lblRecepieFileName.Text = "noname";
            // 
            // toolStripLabel1
            // 
            toolStripLabel1.Alignment = ToolStripItemAlignment.Right;
            toolStripLabel1.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
            toolStripLabel1.Name = "toolStripLabel1";
            toolStripLabel1.Size = new Size(65, 39);
            toolStripLabel1.Text = "Recipe:";
            // 
            // tabControlMain
            // 
            tabControlMain.Controls.Add(tabSystem);
            tabControlMain.Controls.Add(tabProcess);
            tabControlMain.Controls.Add(tabGraphs);
            tabControlMain.Controls.Add(tabLog);
            tabControlMain.Dock = DockStyle.Fill;
            tabControlMain.Location = new Point(3, 69);
            tabControlMain.Margin = new Padding(0);
            tabControlMain.Name = "tabControlMain";
            tabControlMain.SelectedIndex = 0;
            tabControlMain.Size = new Size(1833, 885);
            tabControlMain.TabIndex = 15;
            //
            // tabSystem
            //
            tabSystem.Controls.Add(vacuumSchematic);
            tabSystem.Controls.Add(panelHwRight);
            tabSystem.Controls.Add(panelHwLeft);
            tabSystem.Location = new Point(4, 24);
            tabSystem.Name = "tabSystem";
            tabSystem.Padding = new Padding(3);
            tabSystem.Size = new Size(1825, 857);
            tabSystem.TabIndex = 0;
            tabSystem.Text = "System";
            tabSystem.UseVisualStyleBackColor = true;
            //
            // panelHwLeft
            //
            panelHwLeft.Dock = DockStyle.Left;
            panelHwLeft.Width = 500;
            panelHwLeft.Name = "panelHwLeft";
            panelHwLeft.TabIndex = 10;
            //
            // panelHwRight
            //
            panelHwRight.Dock = DockStyle.Right;
            panelHwRight.Width = 500;
            panelHwRight.Name = "panelHwRight";
            panelHwRight.TabIndex = 11;
            //
            // vacuumSchematic
            //
            vacuumSchematic.Dock = DockStyle.Fill;
            vacuumSchematic.Name = "vacuumSchematic";
            vacuumSchematic.TabIndex = 9;
            // 
            // tabGraphs
            // 
            tabGraphs.Controls.Add(graphLayout);
            tabGraphs.Location = new Point(4, 24);
            tabGraphs.Name = "tabGraphs";
            tabGraphs.Padding = new Padding(3);
            tabGraphs.Size = new Size(1825, 857);
            tabGraphs.TabIndex = 4;
            tabGraphs.Text = "Graphs";
            tabGraphs.UseVisualStyleBackColor = true;
            // 
            // graphLayout
            // 
            graphLayout.ColumnCount = 1;
            graphLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            graphLayout.Dock = DockStyle.Fill;
            graphLayout.Location = new Point(3, 3);
            graphLayout.Name = "graphLayout";
            graphLayout.RowCount = 3;
            graphLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            graphLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            graphLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333321F));
            graphLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            graphLayout.Size = new Size(1819, 851);
            graphLayout.TabIndex = 0;
            // 
            // tabProcess
            // 
            tabProcess.Controls.Add(tableLayoutPanel3);
            tabProcess.Location = new Point(4, 24);
            tabProcess.Name = "tabProcess";
            tabProcess.Padding = new Padding(3);
            tabProcess.Size = new Size(1825, 857);
            tabProcess.TabIndex = 1;
            tabProcess.Text = "Process";
            tabProcess.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel3
            // 
            tableLayoutPanel3.ColumnCount = 3;
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tableLayoutPanel3.Dock = DockStyle.Fill;
            tableLayoutPanel3.Location = new Point(3, 3);
            tableLayoutPanel3.Name = "tableLayoutPanel3";
            tableLayoutPanel3.RowCount = 3;
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayoutPanel3.Size = new Size(1819, 851);
            tableLayoutPanel3.TabIndex = 1;
            // 
            // tabLog
            // 
            tabLog.Controls.Add(logPanelLayout);
            tabLog.Location = new Point(4, 24);
            tabLog.Name = "tabLog";
            tabLog.Padding = new Padding(3);
            tabLog.Size = new Size(1825, 857);
            tabLog.TabIndex = 2;
            tabLog.Text = "Events log";
            tabLog.UseVisualStyleBackColor = true;
            // 
            // logPanelLayout
            // 
            logPanelLayout.ColumnCount = 3;
            logPanelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            logPanelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            logPanelLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            logPanelLayout.Controls.Add(logGridView, 0, 1);
            logPanelLayout.Controls.Add(panel1, 0, 0);
            logPanelLayout.Dock = DockStyle.Fill;
            logPanelLayout.Location = new Point(3, 3);
            logPanelLayout.Name = "logPanelLayout";
            logPanelLayout.RowCount = 2;
            logPanelLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45F));
            logPanelLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            logPanelLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            logPanelLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            logPanelLayout.Size = new Size(1819, 851);
            logPanelLayout.TabIndex = 1;
            // 
            // logGridView
            // 
            logGridView.AllowUserToAddRows = false;
            logGridView.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            logPanelLayout.SetColumnSpan(logGridView, 3);
            logGridView.Dock = DockStyle.Fill;
            logGridView.Location = new Point(3, 48);
            logGridView.Name = "logGridView";
            logGridView.ReadOnly = true;
            logPanelLayout.SetRowSpan(logGridView, 3);
            logGridView.ScrollBars = ScrollBars.Vertical;
            logGridView.Size = new Size(1813, 800);
            logGridView.TabIndex = 0;
            // 
            // panel1
            // 
            logPanelLayout.SetColumnSpan(panel1, 3);
            panel1.Controls.Add(button9);
            panel1.Controls.Add(button6);
            panel1.Controls.Add(button5);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(3, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(1813, 39);
            panel1.TabIndex = 1;
            // 
            // button9
            // 
            button9.Location = new Point(8, 7);
            button9.Name = "button9";
            button9.Size = new Size(91, 25);
            button9.TabIndex = 2;
            button9.Text = "Filter events ...";
            button9.UseVisualStyleBackColor = true;
            // 
            // button6
            // 
            button6.Location = new Point(308, 7);
            button6.Name = "button6";
            button6.Size = new Size(91, 25);
            button6.TabIndex = 1;
            button6.Text = "Clear log";
            button6.UseVisualStyleBackColor = true;
            // 
            // button5
            // 
            button5.Location = new Point(211, 7);
            button5.Name = "button5";
            button5.Size = new Size(91, 25);
            button5.TabIndex = 0;
            button5.Text = "Save to file ...";
            button5.UseVisualStyleBackColor = true;
            // 
            // FrmMain
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1839, 979);
            Controls.Add(tabControlMain);
            Controls.Add(toolStrip1);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "FrmMain";
            Padding = new Padding(3);
            Text = "APS System Control";
            WindowState = FormWindowState.Maximized;
            Load += FrmMain_Load;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            tabControlMain.ResumeLayout(false);
            tabSystem.ResumeLayout(false);
            tabGraphs.ResumeLayout(false);
            tabProcess.ResumeLayout(false);
            tabLog.ResumeLayout(false);
            logPanelLayout.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)logGridView).EndInit();
            panel1.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private StatusStrip statusStrip1;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem newRecepieToolStripMenuItem;
        private ToolStripMenuItem openRecipieToolStripMenuItem;
        private ToolStripMenuItem saveRecepieToolStripMenuItem;
        private ToolStripSeparator toolStripMenuItem1;
        private ToolStripMenuItem exitToolStripMenuItem;
        private ToolStripMenuItem helpToolStripMenuItem;
        private ToolStripMenuItem userManualToolStripMenuItem;
        private ToolStripMenuItem aboutToolStripMenuItem;
        private ToolStripMenuItem recepieToolStripMenuItem;
        private ToolStripMenuItem runToolStripMenuItem;
        private ToolStripSeparator rerunSeparatorToolStripMenuItem;
        private ToolStripMenuItem reRunFromSnapshotToolStripMenuItem;
        private ToolStripMenuItem toolsToolStripMenuItem;
        private ToolStripMenuItem serialConsoleToolStripMenuItem;
        private ToolStripMenuItem serviceToolStripMenuItem;
        private ToolStripMenuItem overridesToolStripMenuItem;
        private ToolStripMenuItem isolateToolStripMenuItem;
        private ToolStripMenuItem settingsToolStripMenuItem;
        private ToolStripMenuItem hwDiagnosticsToolStripMenuItem;
        private ToolStripMenuItem reloadGuiConfigToolStripMenuItem;
        private ToolStripMenuItem hardwareToolStripMenuItem;
        private ToolStrip toolStrip1;
        private ToolStripButton btnSelect;
        private ToolStripSeparator toolStripSeparator1;
        private ToolStripButton btnEdit;
        private ToolStripSeparator toolStripSeparator2;
        private ToolStripButton btnStart;
        private ToolStripButton toolStripButton1;
        private ToolStripProgressBar toolStripProgressBar1;
        private ToolStripLabel lblRecepieFileName;
        private ToolStripLabel toolStripLabel1;
        private DarkTabControl tabControlMain;
        private TabPage tabSystem;
        private VacuumSchematic vacuumSchematic;
        private Panel panelHwLeft;
        private Panel panelHwRight;
        private TabPage tabGraphs;
        private TableLayoutPanel graphLayout;
        private TabPage tabProcess;
        private TableLayoutPanel tableLayoutPanel3;
        private TabPage tabLog;
        private TableLayoutPanel logPanelLayout;
        private DataGridView logGridView;
        private Panel panel1;
        private Button button9;
        private Button button6;
        private Button button5;
    }
}
