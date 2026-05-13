namespace psu_archive_explorer
{
    partial class TextureViewer
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
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.mipLabel = new System.Windows.Forms.Label();
            this.buttonDownMip = new System.Windows.Forms.Button();
            this.buttonUpMip = new System.Windows.Forms.Button();
            this.buttonReplaceMip = new System.Windows.Forms.Button();
            this.loadMipDialog = new System.Windows.Forms.OpenFileDialog();
            this.saveMipDialog = new System.Windows.Forms.SaveFileDialog();
            this.buttonImport = new System.Windows.Forms.Button();
            this.rebuildMipsCheckbox = new System.Windows.Forms.CheckBox();
            this.buttonSaveMip = new System.Windows.Forms.Button();
            this.buttonExport = new System.Windows.Forms.Button();
            this.importTextureDialog = new System.Windows.Forms.OpenFileDialog();
            this.exportTextureDialog = new System.Windows.Forms.SaveFileDialog();
            this.panel1 = new System.Windows.Forms.Panel();
            this.labelPixelFormat = new System.Windows.Forms.Label();
            this.labelPixelFormatValue = new System.Windows.Forms.Label();
            this.labelTextureFormat = new System.Windows.Forms.Label();
            this.labelTextureFormatValue = new System.Windows.Forms.Label();
            this.pixelFormatDropDown = new System.Windows.Forms.ComboBox();
            this.labelSavePixelFormat = new System.Windows.Forms.Label();
            this.groupBoxMip = new System.Windows.Forms.GroupBox();
            this.groupBoxTexture = new System.Windows.Forms.GroupBox();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.panel1.SuspendLayout();
            this.groupBoxMip.SuspendLayout();
            this.groupBoxTexture.SuspendLayout();
            this.SuspendLayout();
            // 
            // pictureBox1
            // 
            // BackColor was previously Lime, which showed through behind images and
            // looked like a rendering bug. Use the panel's normal color so any empty
            // space around an image just looks like background.
            this.pictureBox1.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(366, 277);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;
            // 
            // ============================================================
            //  Top info row: Pixel Format / Texture Format
            // ============================================================
            // 
            // labelPixelFormat
            // 
            this.labelPixelFormat.AutoSize = true;
            this.labelPixelFormat.Location = new System.Drawing.Point(10, 10);
            this.labelPixelFormat.Name = "labelPixelFormat";
            this.labelPixelFormat.Size = new System.Drawing.Size(67, 13);
            this.labelPixelFormat.TabIndex = 11;
            this.labelPixelFormat.Text = "Pixel Format:";
            // 
            // labelPixelFormatValue
            // 
            this.labelPixelFormatValue.AutoSize = true;
            this.labelPixelFormatValue.Location = new System.Drawing.Point(95, 10);
            this.labelPixelFormatValue.Name = "labelPixelFormatValue";
            this.labelPixelFormatValue.Size = new System.Drawing.Size(37, 13);
            this.labelPixelFormatValue.TabIndex = 12;
            this.labelPixelFormatValue.Text = "-";
            // 
            // labelTextureFormat
            // 
            this.labelTextureFormat.AutoSize = true;
            this.labelTextureFormat.Location = new System.Drawing.Point(220, 10);
            this.labelTextureFormat.Name = "labelTextureFormat";
            this.labelTextureFormat.Size = new System.Drawing.Size(81, 13);
            this.labelTextureFormat.TabIndex = 13;
            this.labelTextureFormat.Text = "Texture Format:";
            // 
            // labelTextureFormatValue
            // 
            this.labelTextureFormatValue.AutoSize = true;
            this.labelTextureFormatValue.Location = new System.Drawing.Point(310, 10);
            this.labelTextureFormatValue.Name = "labelTextureFormatValue";
            this.labelTextureFormatValue.Size = new System.Drawing.Size(34, 13);
            this.labelTextureFormatValue.TabIndex = 14;
            this.labelTextureFormatValue.Text = "-";
            // 
            // ============================================================
            //  Group: Current Mipmap (operates on one mip level at a time)
            // ============================================================
            // 
            // groupBoxMip
            // 
            this.groupBoxMip.Controls.Add(this.buttonDownMip);
            this.groupBoxMip.Controls.Add(this.mipLabel);
            this.groupBoxMip.Controls.Add(this.buttonUpMip);
            this.groupBoxMip.Controls.Add(this.buttonReplaceMip);
            this.groupBoxMip.Controls.Add(this.buttonSaveMip);
            this.groupBoxMip.Location = new System.Drawing.Point(7, 35);
            this.groupBoxMip.Name = "groupBoxMip";
            this.groupBoxMip.Size = new System.Drawing.Size(387, 60);
            this.groupBoxMip.TabIndex = 17;
            this.groupBoxMip.TabStop = false;
            this.groupBoxMip.Text = "Current Mipmap";
            // 
            // buttonDownMip
            // 
            this.buttonDownMip.Enabled = false;
            this.buttonDownMip.Location = new System.Drawing.Point(10, 22);
            this.buttonDownMip.Name = "buttonDownMip";
            this.buttonDownMip.Size = new System.Drawing.Size(31, 23);
            this.buttonDownMip.TabIndex = 3;
            this.buttonDownMip.Text = "<<";
            this.buttonDownMip.UseVisualStyleBackColor = true;
            this.buttonDownMip.Click += new System.EventHandler(this.buttonDownMip_Click);
            // 
            // mipLabel
            // 
            this.mipLabel.AutoSize = true;
            this.mipLabel.Location = new System.Drawing.Point(47, 27);
            this.mipLabel.Name = "mipLabel";
            this.mipLabel.Size = new System.Drawing.Size(60, 13);
            this.mipLabel.TabIndex = 2;
            this.mipLabel.Text = "Mip 0 of 0";
            // 
            // buttonUpMip
            // 
            this.buttonUpMip.Enabled = false;
            this.buttonUpMip.Location = new System.Drawing.Point(115, 22);
            this.buttonUpMip.Name = "buttonUpMip";
            this.buttonUpMip.Size = new System.Drawing.Size(33, 23);
            this.buttonUpMip.TabIndex = 4;
            this.buttonUpMip.Text = ">>";
            this.buttonUpMip.UseVisualStyleBackColor = true;
            this.buttonUpMip.Click += new System.EventHandler(this.buttonUpMip_Click);
            // 
            // buttonReplaceMip
            // 
            this.buttonReplaceMip.Location = new System.Drawing.Point(165, 22);
            this.buttonReplaceMip.Name = "buttonReplaceMip";
            this.buttonReplaceMip.Size = new System.Drawing.Size(100, 23);
            this.buttonReplaceMip.TabIndex = 5;
            this.buttonReplaceMip.Text = "Replace Mip...";
            this.buttonReplaceMip.UseVisualStyleBackColor = true;
            this.buttonReplaceMip.Click += new System.EventHandler(this.replaceMipButton_Click);
            // 
            // buttonSaveMip
            // 
            this.buttonSaveMip.Location = new System.Drawing.Point(271, 22);
            this.buttonSaveMip.Name = "buttonSaveMip";
            this.buttonSaveMip.Size = new System.Drawing.Size(100, 23);
            this.buttonSaveMip.TabIndex = 8;
            this.buttonSaveMip.Text = "Export Mip...";
            this.buttonSaveMip.UseVisualStyleBackColor = true;
            this.buttonSaveMip.Click += new System.EventHandler(this.buttonSaveMip_Click);
            // 
            // ============================================================
            //  Image preview panel (scrollable)
            // ============================================================
            // 
            // panel1
            // 
            this.panel1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.panel1.AutoScroll = true;
            this.panel1.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.panel1.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panel1.Controls.Add(this.pictureBox1);
            this.panel1.Location = new System.Drawing.Point(7, 101);
            this.panel1.Name = "panel1";
            this.panel1.Size = new System.Drawing.Size(387, 256);
            this.panel1.TabIndex = 10;
            // 
            // ============================================================
            //  Group: Entire Texture (operates on whole texture / all mips)
            // ============================================================
            // 
            // groupBoxTexture
            // 
            this.groupBoxTexture.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxTexture.Controls.Add(this.buttonImport);
            this.groupBoxTexture.Controls.Add(this.buttonExport);
            this.groupBoxTexture.Controls.Add(this.rebuildMipsCheckbox);
            this.groupBoxTexture.Controls.Add(this.labelSavePixelFormat);
            this.groupBoxTexture.Controls.Add(this.pixelFormatDropDown);
            this.groupBoxTexture.Location = new System.Drawing.Point(400, 35);
            this.groupBoxTexture.Name = "groupBoxTexture";
            this.groupBoxTexture.Size = new System.Drawing.Size(140, 220);
            this.groupBoxTexture.TabIndex = 18;
            this.groupBoxTexture.TabStop = false;
            this.groupBoxTexture.Text = "Entire Texture";
            // 
            // buttonImport
            // 
            this.buttonImport.Location = new System.Drawing.Point(15, 25);
            this.buttonImport.Name = "buttonImport";
            this.buttonImport.Size = new System.Drawing.Size(110, 23);
            this.buttonImport.TabIndex = 6;
            this.buttonImport.Text = "Import Texture...";
            this.buttonImport.UseVisualStyleBackColor = true;
            this.buttonImport.Click += new System.EventHandler(this.importImageButton_Click);
            // 
            // buttonExport
            // 
            this.buttonExport.Location = new System.Drawing.Point(15, 54);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(110, 23);
            this.buttonExport.TabIndex = 9;
            this.buttonExport.Text = "Export Texture...";
            this.buttonExport.UseVisualStyleBackColor = true;
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);
            // 
            // rebuildMipsCheckbox
            // 
            this.rebuildMipsCheckbox.AutoSize = true;
            this.rebuildMipsCheckbox.Checked = true;
            this.rebuildMipsCheckbox.CheckState = System.Windows.Forms.CheckState.Checked;
            this.rebuildMipsCheckbox.Location = new System.Drawing.Point(15, 95);
            this.rebuildMipsCheckbox.Name = "rebuildMipsCheckbox";
            this.rebuildMipsCheckbox.Size = new System.Drawing.Size(115, 17);
            this.rebuildMipsCheckbox.TabIndex = 7;
            this.rebuildMipsCheckbox.Text = "Rebuild mipmaps";
            this.rebuildMipsCheckbox.UseVisualStyleBackColor = true;
            // 
            // labelSavePixelFormat
            // 
            this.labelSavePixelFormat.AutoSize = true;
            this.labelSavePixelFormat.Location = new System.Drawing.Point(12, 140);
            this.labelSavePixelFormat.Name = "labelSavePixelFormat";
            this.labelSavePixelFormat.Size = new System.Drawing.Size(95, 13);
            this.labelSavePixelFormat.TabIndex = 16;
            this.labelSavePixelFormat.Text = "Save Pixel Format:";
            // 
            // pixelFormatDropDown
            // 
            this.pixelFormatDropDown.FormattingEnabled = true;
            this.pixelFormatDropDown.Location = new System.Drawing.Point(15, 156);
            this.pixelFormatDropDown.Name = "pixelFormatDropDown";
            this.pixelFormatDropDown.Size = new System.Drawing.Size(115, 21);
            this.pixelFormatDropDown.TabIndex = 15;
            this.pixelFormatDropDown.SelectedIndexChanged += new System.EventHandler(this.comboBox1_SelectedIndexChanged);
            // 
            // ============================================================
            //  Dialogs
            // ============================================================
            // 
            // loadMipDialog
            // 
            this.loadMipDialog.Filter = "PNG Image|*.png";
            this.loadMipDialog.Title = "Replace this mipmap with a PNG";
            // 
            // saveMipDialog
            // 
            this.saveMipDialog.Filter = "PNG Image|*.png";
            this.saveMipDialog.Title = "Export this mipmap to PNG";
            // 
            // importTextureDialog
            // 
            this.importTextureDialog.FileName = "openFileDialog1";
            this.importTextureDialog.Filter = "PNG Image|*.png|PSU XVR Files|*.xvr|PVRTexTool PVR|*.pvr";
            this.importTextureDialog.Title = "Import a texture (PNG, XVR, or PVR)";
            // 
            // exportTextureDialog
            // 
            // Currently only PNG export is implemented in code, so the filter only
            // offers PNG to avoid confusing users with options that silently fail.
            this.exportTextureDialog.Filter = "PNG Image|*.png";
            this.exportTextureDialog.Title = "Export full texture to PNG";
            // 
            // TextureViewer
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.groupBoxTexture);
            this.Controls.Add(this.groupBoxMip);
            this.Controls.Add(this.labelTextureFormatValue);
            this.Controls.Add(this.labelTextureFormat);
            this.Controls.Add(this.labelPixelFormatValue);
            this.Controls.Add(this.labelPixelFormat);
            this.Controls.Add(this.panel1);
            this.Name = "TextureViewer";
            this.Size = new System.Drawing.Size(550, 365);
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.panel1.ResumeLayout(false);
            this.groupBoxMip.ResumeLayout(false);
            this.groupBoxMip.PerformLayout();
            this.groupBoxTexture.ResumeLayout(false);
            this.groupBoxTexture.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label mipLabel;
        private System.Windows.Forms.Button buttonDownMip;
        private System.Windows.Forms.Button buttonUpMip;
        private System.Windows.Forms.Button buttonReplaceMip;
        private System.Windows.Forms.OpenFileDialog loadMipDialog;
        private System.Windows.Forms.SaveFileDialog saveMipDialog;
        private System.Windows.Forms.Button buttonImport;
        private System.Windows.Forms.CheckBox rebuildMipsCheckbox;
        private System.Windows.Forms.Button buttonSaveMip;
        private System.Windows.Forms.Button buttonExport;
        private System.Windows.Forms.OpenFileDialog importTextureDialog;
        private System.Windows.Forms.SaveFileDialog exportTextureDialog;
        private System.Windows.Forms.Panel panel1;
        private System.Windows.Forms.Label labelPixelFormat;
        private System.Windows.Forms.Label labelPixelFormatValue;
        private System.Windows.Forms.Label labelTextureFormat;
        private System.Windows.Forms.Label labelTextureFormatValue;
        private System.Windows.Forms.ComboBox pixelFormatDropDown;
        private System.Windows.Forms.Label labelSavePixelFormat;
        private System.Windows.Forms.GroupBox groupBoxMip;
        private System.Windows.Forms.GroupBox groupBoxTexture;
    }
}