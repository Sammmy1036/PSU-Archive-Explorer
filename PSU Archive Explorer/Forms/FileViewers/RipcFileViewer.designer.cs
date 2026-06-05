namespace psu_archive_explorer.FileViewers
{
    partial class RipcFileViewer
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        private void InitializeComponent()
        {
            // ── Instantiate ──────────────────────────────────────────────────────
            this.labelPixelFormat = new System.Windows.Forms.Label();
            this.labelPixelFormatValue = new System.Windows.Forms.Label();
            this.labelTextureFormat = new System.Windows.Forms.Label();
            this.labelTextureFormatValue = new System.Windows.Forms.Label();
            this.labelImageSize = new System.Windows.Forms.Label();
            this.labelImageSizeValue = new System.Windows.Forms.Label();

            this.groupBoxMip = new System.Windows.Forms.GroupBox();
            this.scrollPanel = new System.Windows.Forms.Panel();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.labelZoom = new System.Windows.Forms.Label();
            this.zoomTrackBar = new System.Windows.Forms.TrackBar();
            this.zoomValueLabel = new System.Windows.Forms.Label();

            this.groupBoxTexture = new System.Windows.Forms.GroupBox();
            this.buttonImport = new System.Windows.Forms.Button();
            this.buttonExport = new System.Windows.Forms.Button();
            this.buttonCopy = new System.Windows.Forms.Button();
            this.buttonExportPal = new System.Windows.Forms.Button();

            this.labelPaletteTitle = new System.Windows.Forms.Label();
            this.palettePanel = new System.Windows.Forms.Panel();

            this.importTextureDialog = new System.Windows.Forms.OpenFileDialog();
            this.exportTextureDialog = new System.Windows.Forms.SaveFileDialog();
            this.exportPaletteDialog = new System.Windows.Forms.SaveFileDialog();

            // ── Begin suspend ────────────────────────────────────────────────────
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.zoomTrackBar)).BeginInit();
            this.groupBoxMip.SuspendLayout();
            this.groupBoxTexture.SuspendLayout();
            this.SuspendLayout();

            // ════════════════════════════════════════════════════════════════════
            //  Info row — Pixel Format | Texture Format | Image Size
            //  All anchored Top|Left so they stay put on resize.
            // ════════════════════════════════════════════════════════════════════

            this.labelPixelFormat.AutoSize = true;
            this.labelPixelFormat.Location = new System.Drawing.Point(10, 10);
            this.labelPixelFormat.Name = "labelPixelFormat";
            this.labelPixelFormat.TabIndex = 20;
            this.labelPixelFormat.Text = "Pixel Format:";

            this.labelPixelFormatValue.AutoSize = true;
            this.labelPixelFormatValue.Location = new System.Drawing.Point(95, 10);
            this.labelPixelFormatValue.Name = "labelPixelFormatValue";
            this.labelPixelFormatValue.TabIndex = 21;
            this.labelPixelFormatValue.Text = "-";

            this.labelTextureFormat.AutoSize = true;
            this.labelTextureFormat.Location = new System.Drawing.Point(220, 10);
            this.labelTextureFormat.Name = "labelTextureFormat";
            this.labelTextureFormat.TabIndex = 22;
            this.labelTextureFormat.Text = "Texture Format:";

            this.labelTextureFormatValue.AutoSize = true;
            this.labelTextureFormatValue.Location = new System.Drawing.Point(310, 10);
            this.labelTextureFormatValue.Name = "labelTextureFormatValue";
            this.labelTextureFormatValue.TabIndex = 23;
            this.labelTextureFormatValue.Text = "-";

            this.labelImageSize.AutoSize = true;
            this.labelImageSize.Location = new System.Drawing.Point(430, 10);
            this.labelImageSize.Name = "labelImageSize";
            this.labelImageSize.TabIndex = 24;
            this.labelImageSize.Text = "Size:";

            this.labelImageSizeValue.AutoSize = true;
            this.labelImageSizeValue.Location = new System.Drawing.Point(465, 10);
            this.labelImageSizeValue.Name = "labelImageSizeValue";
            this.labelImageSizeValue.TabIndex = 25;
            this.labelImageSizeValue.Text = "-";

            // ════════════════════════════════════════════════════════════════════
            //  groupBoxMip — "Current View"
            //  Anchored Top|Left|Right so it stretches horizontally with the window.
            //  Inside: scrollPanel anchors all 4 sides (minus bottom zoom strip),
            //  zoom controls anchor Bottom|Left so they stay at the bottom of the box.
            //  Fixed height of 290 px — palette fills the rest below.
            // ════════════════════════════════════════════════════════════════════

            // pictureBox1 — AutoSize, lives inside scrollPanel
            this.pictureBox1.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.pictureBox1.Location = new System.Drawing.Point(0, 0);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(200, 200);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.AutoSize;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = false;

            // scrollPanel — anchors all 4 sides, bottom edge sits 46px above groupbox bottom
            // (zoom trackbar ~27px tall + 12px gap above + 7px below = 46px)
            this.scrollPanel.AutoScroll = true;
            this.scrollPanel.BackColor = System.Drawing.SystemColors.AppWorkspace;
            this.scrollPanel.Anchor =
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right |
                System.Windows.Forms.AnchorStyles.Bottom;
            this.scrollPanel.Location = new System.Drawing.Point(6, 19);
            this.scrollPanel.Name = "scrollPanel";
            // Height = groupBoxMip(380) - top(19) - zoom strip(46) = 315
            this.scrollPanel.Size = new System.Drawing.Size(521, 315);
            this.scrollPanel.TabIndex = 0;
            this.scrollPanel.Controls.Add(this.pictureBox1);

            // labelZoom — raised 12px vs previous position
            this.labelZoom.AutoSize = true;
            this.labelZoom.Anchor =
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left;
            this.labelZoom.Location = new System.Drawing.Point(6, 337);
            this.labelZoom.Name = "labelZoom";
            this.labelZoom.TabIndex = 1;
            this.labelZoom.Text = "Zoom:";

            // zoomTrackBar — raised 12px
            this.zoomTrackBar.Anchor =
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left;
            this.zoomTrackBar.Location = new System.Drawing.Point(48, 332);
            this.zoomTrackBar.Minimum = 1;
            this.zoomTrackBar.Maximum = 12;
            this.zoomTrackBar.Value = 4;
            this.zoomTrackBar.TickFrequency = 1;
            this.zoomTrackBar.SmallChange = 1;
            this.zoomTrackBar.LargeChange = 2;
            this.zoomTrackBar.Width = 220;
            this.zoomTrackBar.Name = "zoomTrackBar";
            this.zoomTrackBar.TabIndex = 2;
            this.zoomTrackBar.Scroll += new System.EventHandler(this.zoomTrackBar_Scroll);

            // zoomValueLabel — raised 12px
            this.zoomValueLabel.AutoSize = true;
            this.zoomValueLabel.Anchor =
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left;
            this.zoomValueLabel.Location = new System.Drawing.Point(273, 337);
            this.zoomValueLabel.Name = "zoomValueLabel";
            this.zoomValueLabel.TabIndex = 3;
            this.zoomValueLabel.Text = "4×";

            // groupBoxMip — anchored Top|Left|Right; height managed by ResizePaletteBox
            this.groupBoxMip.Anchor =
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.groupBoxMip.Controls.Add(this.scrollPanel);
            this.groupBoxMip.Controls.Add(this.labelZoom);
            this.groupBoxMip.Controls.Add(this.zoomTrackBar);
            this.groupBoxMip.Controls.Add(this.zoomValueLabel);
            this.groupBoxMip.Location = new System.Drawing.Point(7, 30);
            this.groupBoxMip.Name = "groupBoxMip";
            this.groupBoxMip.Size = new System.Drawing.Size(533, 380);
            this.groupBoxMip.TabIndex = 17;
            this.groupBoxMip.TabStop = false;
            this.groupBoxMip.Text = "Current View";

            // ════════════════════════════════════════════════════════════════════
            //  groupBoxTexture — "Entire Texture" right-column buttons
            //  Anchored Top|Right so it stays against the right edge.
            //  Positioned to the right of groupBoxMip — but since groupBoxMip now
            //  takes the full width, groupBoxTexture floats over the top-right area
            //  outside the mip box (same pattern as XvrViewer).
            // ════════════════════════════════════════════════════════════════════

            this.buttonImport.Location = new System.Drawing.Point(15, 25);
            this.buttonImport.Name = "buttonImport";
            this.buttonImport.Size = new System.Drawing.Size(110, 23);
            this.buttonImport.TabIndex = 6;
            this.buttonImport.Text = "Import Texture...";
            this.buttonImport.UseVisualStyleBackColor = true;
            this.buttonImport.Click += new System.EventHandler(this.buttonImport_Click);

            this.buttonExport.Location = new System.Drawing.Point(15, 54);
            this.buttonExport.Name = "buttonExport";
            this.buttonExport.Size = new System.Drawing.Size(110, 23);
            this.buttonExport.TabIndex = 7;
            this.buttonExport.Text = "Export Texture...";
            this.buttonExport.UseVisualStyleBackColor = true;
            this.buttonExport.Click += new System.EventHandler(this.buttonExport_Click);

            this.buttonCopy.Location = new System.Drawing.Point(15, 83);
            this.buttonCopy.Name = "buttonCopy";
            this.buttonCopy.Size = new System.Drawing.Size(110, 23);
            this.buttonCopy.TabIndex = 8;
            this.buttonCopy.Text = "Copy to Clipboard";
            this.buttonCopy.UseVisualStyleBackColor = true;
            this.buttonCopy.Click += new System.EventHandler(this.buttonCopy_Click);

            this.buttonExportPal.Location = new System.Drawing.Point(15, 119);
            this.buttonExportPal.Name = "buttonExportPal";
            this.buttonExportPal.Size = new System.Drawing.Size(110, 23);
            this.buttonExportPal.TabIndex = 9;
            this.buttonExportPal.Text = "Export Palette...";
            this.buttonExportPal.UseVisualStyleBackColor = true;
            this.buttonExportPal.Click += new System.EventHandler(this.buttonExportPal_Click);

            this.groupBoxTexture.Anchor =
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Right;
            this.groupBoxTexture.Controls.Add(this.buttonImport);
            this.groupBoxTexture.Controls.Add(this.buttonExport);
            this.groupBoxTexture.Controls.Add(this.buttonCopy);
            this.groupBoxTexture.Controls.Add(this.buttonExportPal);
            this.groupBoxTexture.Location = new System.Drawing.Point(548, 30);
            this.groupBoxTexture.Name = "groupBoxTexture";
            this.groupBoxTexture.Size = new System.Drawing.Size(140, 155);
            this.groupBoxTexture.TabIndex = 18;
            this.groupBoxTexture.TabStop = false;
            this.groupBoxTexture.Text = "Entire Texture";

            // ════════════════════════════════════════════════════════════════════
            //  groupBoxPalette — anchored all 4 sides, fills space below groupBoxMip
            //  Top = groupBoxMip.Bottom + 5 = 30 + 283 + 5 = 318
            // ════════════════════════════════════════════════════════════════════

            // ── Palette title label + panel (no GroupBox — avoids chrome/padding bugs)

            this.labelPaletteTitle.AutoSize = false;
            this.labelPaletteTitle.Anchor =
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.labelPaletteTitle.BackColor = System.Drawing.SystemColors.Control;
            this.labelPaletteTitle.Location = new System.Drawing.Point(7, 416);
            this.labelPaletteTitle.Name = "labelPaletteTitle";
            this.labelPaletteTitle.Size = new System.Drawing.Size(533, 16);
            this.labelPaletteTitle.TabIndex = 24;
            this.labelPaletteTitle.Text = "Colour Palette  (click a swatch to recolour)";

            this.palettePanel.BackColor = System.Drawing.SystemColors.Control;
            this.palettePanel.Anchor =
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right;
            this.palettePanel.Location = new System.Drawing.Point(7, 432);
            this.palettePanel.Name = "palettePanel";
            this.palettePanel.Size = new System.Drawing.Size(533, 40);   // height set at runtime
            this.palettePanel.TabIndex = 25;
            this.palettePanel.Paint += new System.Windows.Forms.PaintEventHandler(this.palettePanel_Paint);
            this.palettePanel.MouseMove += new System.Windows.Forms.MouseEventHandler(this.palettePanel_MouseMove);
            this.palettePanel.MouseLeave += new System.EventHandler(this.palettePanel_MouseLeave);
            this.palettePanel.MouseClick += new System.Windows.Forms.MouseEventHandler(this.palettePanel_MouseClick);

            // ════════════════════════════════════════════════════════════════════
            //  Dialogs
            // ════════════════════════════════════════════════════════════════════

            this.importTextureDialog.Filter = "Image Files|*.png;*.bmp|PNG Image|*.png|BMP Image|*.bmp";
            this.importTextureDialog.Title = "Import replacement texture (must match original dimensions)";

            this.exportTextureDialog.Filter = "PNG Image|*.png|BMP Image|*.bmp";
            this.exportTextureDialog.Title = "Export texture";
            this.exportTextureDialog.FileName = "ripc_export";

            this.exportPaletteDialog.Filter = "Adobe Colour Table|*.act|All Files|*.*";
            this.exportPaletteDialog.Title = "Export colour palette";
            this.exportPaletteDialog.FileName = "ripc_palette.act";

            // ════════════════════════════════════════════════════════════════════
            //  UserControl — total width = groupBoxMip(533) + gap(7) + Entire Texture(140) + margin = 700
            // ════════════════════════════════════════════════════════════════════

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.labelPixelFormat);
            this.Controls.Add(this.labelPixelFormatValue);
            this.Controls.Add(this.labelTextureFormat);
            this.Controls.Add(this.labelTextureFormatValue);
            this.Controls.Add(this.labelImageSize);
            this.Controls.Add(this.labelImageSizeValue);
            this.Controls.Add(this.groupBoxMip);
            this.Controls.Add(this.groupBoxTexture);
            this.Controls.Add(this.labelPaletteTitle);
            this.Controls.Add(this.palettePanel);
            this.Name = "RipcFileViewer";
            this.Size = new System.Drawing.Size(700, 500);
            this.Resize += new System.EventHandler((s, ev) => ResizePaletteBox());

            // ── End suspend ──────────────────────────────────────────────────────
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.zoomTrackBar)).EndInit();
            this.groupBoxMip.ResumeLayout(false);
            this.groupBoxMip.PerformLayout();
            this.groupBoxTexture.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        // ── Field declarations ────────────────────────────────────────────────

        private System.Windows.Forms.Label labelPixelFormat;
        private System.Windows.Forms.Label labelPixelFormatValue;
        private System.Windows.Forms.Label labelTextureFormat;
        private System.Windows.Forms.Label labelTextureFormatValue;
        private System.Windows.Forms.Label labelImageSize;
        private System.Windows.Forms.Label labelImageSizeValue;

        private System.Windows.Forms.GroupBox groupBoxMip;
        private System.Windows.Forms.Panel scrollPanel;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.Label labelZoom;
        private System.Windows.Forms.TrackBar zoomTrackBar;
        private System.Windows.Forms.Label zoomValueLabel;

        private System.Windows.Forms.GroupBox groupBoxTexture;
        private System.Windows.Forms.Button buttonImport;
        private System.Windows.Forms.Button buttonExport;
        private System.Windows.Forms.Button buttonCopy;
        private System.Windows.Forms.Button buttonExportPal;

        private System.Windows.Forms.Label labelPaletteTitle;
        private System.Windows.Forms.Panel palettePanel;

        private System.Windows.Forms.OpenFileDialog importTextureDialog;
        private System.Windows.Forms.SaveFileDialog exportTextureDialog;
        private System.Windows.Forms.SaveFileDialog exportPaletteDialog;
    }
}