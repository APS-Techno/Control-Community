namespace APS.SystemControl.Forms
{
    partial class frmEditor
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(frmEditor));
            statusStrip1 = new StatusStrip();
            toolStripStatusLabelCaretPosition = new ToolStripStatusLabel();
            toolStrip1 = new ToolStrip();
            toolStripButton1 = new ToolStripButton();
            tableLayout = new TableLayoutPanel();
            panelLineNumbers = new Panel();
            toolStripButton2 = new ToolStripButton();
            statusStrip1.SuspendLayout();
            toolStrip1.SuspendLayout();
            tableLayout.SuspendLayout();
            SuspendLayout();
            // 
            // statusStrip1
            // 
            statusStrip1.Items.AddRange(new ToolStripItem[] { toolStripStatusLabelCaretPosition });
            statusStrip1.Location = new Point(3, 631);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Size = new Size(991, 22);
            statusStrip1.TabIndex = 0;
            statusStrip1.Text = "statusStrip1";
            // 
            // toolStripStatusLabelCaretPosition
            // 
            toolStripStatusLabelCaretPosition.Name = "toolStripStatusLabelCaretPosition";
            toolStripStatusLabelCaretPosition.Size = new Size(108, 17);
            toolStripStatusLabelCaretPosition.Text = "Line:   0  Column: 0";
            // 
            // toolStrip1
            // 
            toolStrip1.ImageScalingSize = new Size(32, 32);
            toolStrip1.Items.AddRange(new ToolStripItem[] { toolStripButton1, toolStripButton2 });
            toolStrip1.Location = new Point(3, 3);
            toolStrip1.Name = "toolStrip1";
            toolStrip1.Size = new Size(991, 39);
            toolStrip1.TabIndex = 1;
            toolStrip1.Text = "toolStrip1";
            // 
            // toolStripButton1
            // 
            toolStripButton1.Alignment = ToolStripItemAlignment.Right;
            toolStripButton1.Image = (Image)resources.GetObject("toolStripButton1.Image");
            toolStripButton1.ImageTransparentColor = Color.Magenta;
            toolStripButton1.Name = "toolStripButton1";
            toolStripButton1.Size = new Size(67, 36);
            toolStripButton1.Text = "Save";
            toolStripButton1.Click += toolStripButton1_Click;
            // 
            // tableLayout
            // 
            tableLayout.ColumnCount = 2;
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30F));
            tableLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayout.Controls.Add(panelLineNumbers, 0, 0);
            tableLayout.Dock = DockStyle.Fill;
            tableLayout.Location = new Point(3, 42);
            tableLayout.Name = "tableLayout";
            tableLayout.RowCount = 1;
            tableLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            tableLayout.Size = new Size(991, 589);
            tableLayout.TabIndex = 4;
            // 
            // panelLineNumbers
            // 
            panelLineNumbers.Dock = DockStyle.Left;
            panelLineNumbers.Location = new Point(3, 3);
            panelLineNumbers.Name = "panelLineNumbers";
            panelLineNumbers.Size = new Size(24, 583);
            panelLineNumbers.TabIndex = 4;
            // 
            // toolStripButton2
            // 
            toolStripButton2.DisplayStyle = ToolStripItemDisplayStyle.Image;
            toolStripButton2.Image = (Image)resources.GetObject("toolStripButton2.Image");
            toolStripButton2.ImageTransparentColor = Color.Magenta;
            toolStripButton2.Name = "toolStripButton2";
            toolStripButton2.Size = new Size(36, 36);
            toolStripButton2.Text = "toolStripButton2";
            toolStripButton2.Click += toolStripButton2_Click;
            // 
            // frmEditor
            // 
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(997, 656);
            Controls.Add(tableLayout);
            Controls.Add(toolStrip1);
            Controls.Add(statusStrip1);
            Name = "frmEditor";
            Padding = new Padding(3);
            Text = "Edit Recipe";
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            toolStrip1.ResumeLayout(false);
            toolStrip1.PerformLayout();
            tableLayout.ResumeLayout(false);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private StatusStrip statusStrip1;
        private ToolStrip toolStrip1;
        private ToolStripButton toolStripButton1;
        private ToolStripStatusLabel toolStripStatusLabelCaretPosition;
        private TableLayoutPanel tableLayout;
        private Panel panelLineNumbers;
        private ToolStripButton toolStripButton2;
    }
}