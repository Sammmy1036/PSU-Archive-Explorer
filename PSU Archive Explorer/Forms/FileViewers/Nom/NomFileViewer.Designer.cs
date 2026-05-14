namespace psu_archive_explorer
{
    partial class NomFileViewer
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

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        ///
        /// Layout: two docked regions, top-to-bottom.
        ///   1) summaryPanel  — Top, 60 px tall. Light yellow banner that
        ///                       acts as a unified header. Filename + stats
        ///                       stack on the left; Export button anchors
        ///                       top-right (same role/weight as the parent
        ///                       form's "View Current File in Hex").
        ///   2) splitContainer1 — Fill. Tree on the left, frame data on the
        ///                         right. Right box uses Consolas so the
        ///                         tabular frame dump aligns properly.
        /// </summary>
        private void InitializeComponent()
        {
            this.summaryPanel = new System.Windows.Forms.Panel();
            this.exportGlbButton = new System.Windows.Forms.Button();
            this.summaryLabel = new System.Windows.Forms.Label();
            this.fileNameLabel = new System.Windows.Forms.Label();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.frameDataTextBox = new System.Windows.Forms.RichTextBox();
            this.summaryPanel.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.SuspendLayout();
            //
            // summaryPanel
            //
            // The three children use absolute positioning (with anchors) rather
            // than docking. Docking the labels would fight the button's anchor
            // or require nesting yet another sub-panel; absolute + anchor keeps
            // the structure flat and the layout predictable on resize.
            this.summaryPanel.BackColor = System.Drawing.SystemColors.Info;
            this.summaryPanel.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.summaryPanel.Controls.Add(this.exportGlbButton);
            this.summaryPanel.Controls.Add(this.fileNameLabel);
            this.summaryPanel.Controls.Add(this.summaryLabel);
            this.summaryPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.summaryPanel.Location = new System.Drawing.Point(0, 0);
            this.summaryPanel.Name = "summaryPanel";
            this.summaryPanel.Size = new System.Drawing.Size(422, 60);
            this.summaryPanel.TabIndex = 0;
            //
            // exportGlbButton
            //
            // Anchored Top|Right so it stays at the right edge when the viewer
            // is resized. Width and font set to roughly match the parent
            // form's "View Current File in Hex" button so the two read as a
            // visual pair.
            this.exportGlbButton.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.exportGlbButton.BackColor = System.Drawing.SystemColors.Control;
            this.exportGlbButton.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Regular);
            this.exportGlbButton.Location = new System.Drawing.Point(265, 16);
            this.exportGlbButton.Name = "exportGlbButton";
            this.exportGlbButton.Size = new System.Drawing.Size(151, 28);
            this.exportGlbButton.TabIndex = 0;
            this.exportGlbButton.Text = "Export Animation to GLB";
            this.exportGlbButton.UseVisualStyleBackColor = false;
            this.exportGlbButton.Click += new System.EventHandler(this.exportGlbButton_Click);
            //
            // fileNameLabel
            //
            // Top|Left|Right anchor so the filename grows with the panel but
            // stops short of the button on the right side.
            this.fileNameLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.fileNameLabel.AutoEllipsis = true;
            this.fileNameLabel.Font = new System.Drawing.Font("Segoe UI Semibold", 9F, System.Drawing.FontStyle.Bold);
            this.fileNameLabel.Location = new System.Drawing.Point(8, 8);
            this.fileNameLabel.Name = "fileNameLabel";
            this.fileNameLabel.Size = new System.Drawing.Size(251, 18);
            this.fileNameLabel.TabIndex = 1;
            this.fileNameLabel.Text = "(filename)";
            this.fileNameLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // summaryLabel
            //
            this.summaryLabel.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
                        | System.Windows.Forms.AnchorStyles.Right)));
            this.summaryLabel.AutoEllipsis = true;
            this.summaryLabel.Font = new System.Drawing.Font("Segoe UI", 8.25F);
            this.summaryLabel.ForeColor = System.Drawing.SystemColors.ControlDarkDark;
            this.summaryLabel.Location = new System.Drawing.Point(8, 30);
            this.summaryLabel.Name = "summaryLabel";
            this.summaryLabel.Size = new System.Drawing.Size(251, 20);
            this.summaryLabel.TabIndex = 2;
            this.summaryLabel.Text = "(summary)";
            this.summaryLabel.TextAlign = System.Drawing.ContentAlignment.MiddleLeft;
            //
            // splitContainer1
            //
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(0, 60);
            this.splitContainer1.Name = "splitContainer1";
            //
            // splitContainer1.Panel1
            //
            this.splitContainer1.Panel1.Controls.Add(this.treeView1);
            //
            // splitContainer1.Panel2
            //
            this.splitContainer1.Panel2.Controls.Add(this.frameDataTextBox);
            this.splitContainer1.Size = new System.Drawing.Size(422, 274);
            this.splitContainer1.SplitterDistance = 180;
            this.splitContainer1.TabIndex = 1;
            //
            // treeView1
            //
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Font = new System.Drawing.Font("Segoe UI", 9F);
            this.treeView1.HideSelection = false;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(180, 274);
            this.treeView1.TabIndex = 0;
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);
            //
            // frameDataTextBox
            //
            this.frameDataTextBox.BackColor = System.Drawing.SystemColors.Window;
            this.frameDataTextBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.frameDataTextBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.frameDataTextBox.Font = new System.Drawing.Font("Consolas", 9F);
            this.frameDataTextBox.Location = new System.Drawing.Point(0, 0);
            this.frameDataTextBox.Name = "frameDataTextBox";
            this.frameDataTextBox.ReadOnly = true;
            this.frameDataTextBox.Size = new System.Drawing.Size(238, 274);
            this.frameDataTextBox.TabIndex = 0;
            this.frameDataTextBox.Text = "";
            this.frameDataTextBox.WordWrap = false;
            //
            // NomFileViewer
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            // Reverse z-order: the splitContainer is added first (so Fill claims
            // remaining space), summaryPanel last (so its Top dock takes priority).
            this.Controls.Add(this.splitContainer1);
            this.Controls.Add(this.summaryPanel);
            this.Name = "NomFileViewer";
            this.Size = new System.Drawing.Size(422, 334);
            this.summaryPanel.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel summaryPanel;
        private System.Windows.Forms.Button exportGlbButton;
        private System.Windows.Forms.Label fileNameLabel;
        private System.Windows.Forms.Label summaryLabel;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.RichTextBox frameDataTextBox;
    }
}