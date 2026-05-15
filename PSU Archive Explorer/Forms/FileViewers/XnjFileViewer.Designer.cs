namespace psu_archive_explorer.FileViewers
{
    partial class XnjFileViewer
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
        /// </summary>
        private void InitializeComponent()
        {
            this.mainSplit = new System.Windows.Forms.SplitContainer();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.propertiesGrid = new System.Windows.Forms.DataGridView();
            this.colProperty = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.colValue = new System.Windows.Forms.DataGridViewTextBoxColumn();
            this.statusLabel = new System.Windows.Forms.Label();
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).BeginInit();
            this.mainSplit.Panel1.SuspendLayout();
            this.mainSplit.Panel2.SuspendLayout();
            this.mainSplit.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.propertiesGrid)).BeginInit();
            this.SuspendLayout();
            //
            // mainSplit
            //
            // Dock=Fill (NOT Anchor) so the split container always fills the
            // viewer regardless of what size the host panel resizes us to.
            // This matches PointeredFileViewer / ListFileViewer; the Anchor
            // approach collapsed the contents to zero-area when the host
            // panel size differed from the design-time size.
            this.mainSplit.Dock = System.Windows.Forms.DockStyle.Fill;
            this.mainSplit.Location = new System.Drawing.Point(0, 23);
            this.mainSplit.Name = "mainSplit";
            //
            // mainSplit.Panel1 (tree)
            //
            this.mainSplit.Panel1.Controls.Add(this.treeView1);
            //
            // mainSplit.Panel2 (properties grid)
            //
            this.mainSplit.Panel2.Controls.Add(this.propertiesGrid);
            this.mainSplit.Size = new System.Drawing.Size(720, 457);
            this.mainSplit.SplitterDistance = 280;
            this.mainSplit.TabIndex = 0;
            //
            // treeView1
            //
            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.HideSelection = false;
            this.treeView1.Location = new System.Drawing.Point(0, 0);
            this.treeView1.Name = "treeView1";
            this.treeView1.Size = new System.Drawing.Size(280, 457);
            this.treeView1.TabIndex = 0;
            //
            // propertiesGrid
            //
            this.propertiesGrid.AllowUserToAddRows = false;
            this.propertiesGrid.AllowUserToDeleteRows = false;
            this.propertiesGrid.AllowUserToResizeRows = false;
            this.propertiesGrid.AutoSizeColumnsMode = System.Windows.Forms.DataGridViewAutoSizeColumnsMode.Fill;
            this.propertiesGrid.AutoSizeRowsMode = System.Windows.Forms.DataGridViewAutoSizeRowsMode.AllCells;
            this.propertiesGrid.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            this.propertiesGrid.Columns.AddRange(new System.Windows.Forms.DataGridViewColumn[] {
            this.colProperty,
            this.colValue});
            this.propertiesGrid.Dock = System.Windows.Forms.DockStyle.Fill;
            this.propertiesGrid.Location = new System.Drawing.Point(0, 0);
            this.propertiesGrid.Name = "propertiesGrid";
            this.propertiesGrid.ReadOnly = true;
            this.propertiesGrid.RowHeadersVisible = false;
            this.propertiesGrid.Size = new System.Drawing.Size(436, 457);
            this.propertiesGrid.TabIndex = 0;
            //
            // colProperty
            //
            this.colProperty.FillWeight = 40F;
            this.colProperty.HeaderText = "Property";
            this.colProperty.Name = "colProperty";
            this.colProperty.ReadOnly = true;
            //
            // colValue
            //
            this.colValue.FillWeight = 60F;
            this.colValue.HeaderText = "Value";
            this.colValue.Name = "colValue";
            this.colValue.ReadOnly = true;
            //
            // statusLabel
            //
            this.statusLabel.Dock = System.Windows.Forms.DockStyle.Top;
            this.statusLabel.Location = new System.Drawing.Point(0, 0);
            this.statusLabel.Name = "statusLabel";
            this.statusLabel.Padding = new System.Windows.Forms.Padding(4, 4, 4, 4);
            this.statusLabel.Size = new System.Drawing.Size(720, 23);
            this.statusLabel.TabIndex = 1;
            this.statusLabel.Text = "";
            //
            // XnjFileViewer
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            // Add mainSplit FIRST (it will fill the leftover space), then
            // statusLabel (docks to the top strip). With docking, controls
            // added later are positioned before those added earlier, so a
            // Fill control added first ends up with the area left after the
            // Top-docked label.
            this.Controls.Add(this.mainSplit);
            this.Controls.Add(this.statusLabel);
            this.Name = "XnjFileViewer";
            this.Size = new System.Drawing.Size(720, 480);
            this.mainSplit.Panel1.ResumeLayout(false);
            this.mainSplit.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.mainSplit)).EndInit();
            this.mainSplit.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.propertiesGrid)).EndInit();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.SplitContainer mainSplit;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.DataGridView propertiesGrid;
        private System.Windows.Forms.DataGridViewTextBoxColumn colProperty;
        private System.Windows.Forms.DataGridViewTextBoxColumn colValue;
        private System.Windows.Forms.Label statusLabel;
    }
}