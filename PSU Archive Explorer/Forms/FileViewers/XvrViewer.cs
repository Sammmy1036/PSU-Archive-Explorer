using System;
using System.Drawing;
using System.Windows.Forms;
using System.IO;
using PSULib.FileClasses.Textures.XVR;
using PSULib.FileClasses.Textures;
using PSULib.FileClasses.General;

namespace psu_archive_explorer
{
    public partial class TextureViewer : UserControl
    {
        ITextureFile internalTexture;
        int currentMip = 0;
        int mipCount = 0;
        string filename = "";
        private PsuTexturePixelFormat[] permittedFormats = { PsuTexturePixelFormat.Argb1555, PsuTexturePixelFormat.Rgb555, PsuTexturePixelFormat.Argb4444, PsuTexturePixelFormat.Rgb565, PsuTexturePixelFormat.Argb8888, PsuTexturePixelFormat.Xrgb8888, PsuTexturePixelFormat.Rgb655, PsuTexturePixelFormat.Rgba8888, PsuTexturePixelFormat.Abgr8888 };

        TextureViewer()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            SetupTooltips();
        }

        public TextureViewer(ITextureFile toLoad) : this()
        {
            if (toLoad is PsuFile)
            {
                filename = (toLoad as PsuFile).filename;
            }
            if (toLoad is XvrTextureFile)
            {
                labelPixelFormatValue.Text = (toLoad as XvrTextureFile).OriginalPixelFormat.ToString();
                labelTextureFormatValue.Text = (toLoad as XvrTextureFile).OriginalTextureType.ToString();
                labelTextureFormatValue.Text = (toLoad as XvrTextureFile).SaveTextureType.ToString();
                if ((toLoad as XvrTextureFile).SaveTextureType != (toLoad as XvrTextureFile).OriginalTextureType)
                {
                    labelTextureFormatValue.ForeColor = Color.Green;
                }
                pixelFormatDropDown.DataSource = permittedFormats;
                pixelFormatDropDown.SelectedItem = (toLoad as XvrTextureFile).SavePixelFormat;
            }
            else if (toLoad is GimTextureFile)
            {
                labelPixelFormatValue.Text = (toLoad as GimTextureFile).palFormat.ToString();
                labelTextureFormatValue.Text = (toLoad as GimTextureFile).dataFormat.ToString();
            }
            internalTexture = toLoad;
            currentMip = 0;
            mipCount = internalTexture.mipMaps.Length;
            refreshMip();
        }

        private void SetupTooltips()
        {
            ToolTip tip = new ToolTip();
            tip.AutoPopDelay = 10000;
            tip.InitialDelay = 400;
            tip.ReshowDelay = 200;
            tip.ShowAlways = true;

            tip.SetToolTip(buttonReplaceMip, "Replace ONLY the currently displayed mipmap with a PNG image. The rest of the texture is left untouched.");
            tip.SetToolTip(buttonSaveMip, "Save ONLY the currently displayed mipmap to a PNG file.");
            tip.SetToolTip(buttonDownMip, "Show the previous (larger) mipmap level.");
            tip.SetToolTip(buttonUpMip, "Show the next (smaller) mipmap level.");

            tip.SetToolTip(buttonImport, "Replace the ENTIRE texture from a PNG or XVR file. If 'Rebuild Mips' is checked, smaller mip levels will be generated automatically from a PNG.");
            tip.SetToolTip(buttonExport, "Export the full-size (largest) mipmap of this texture to a PNG file.");
            tip.SetToolTip(rebuildMipsCheckbox, "When importing a PNG, automatically generate all the smaller mipmap levels by downscaling.");
            tip.SetToolTip(pixelFormatDropDown, "The pixel format the texture will be saved as when written back to the archive.");
        }

        private void replaceMipButton_Click(object sender, EventArgs e)
        {
            if (loadMipDialog.ShowDialog() != DialogResult.OK)
                return;

            Bitmap replacementImage;
            try
            {
                replacementImage = new Bitmap(loadMipDialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read the selected image file:\n" + ex.Message, "Image Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Match the size validation Import already has, so users don't crash the game.
            if (!isPowerOfTwo(replacementImage.Width) || !isPowerOfTwo(replacementImage.Height))
            {
                MessageBox.Show("Mipmap width and height must each be a power of 2.\nAllowed sizes: 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024.", "Image Size Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (internalTexture is XvrTextureFile)
            {
                (internalTexture as XvrTextureFile).SaveTextureType = PsuTextureType.Raster;
            }
            internalTexture.ReplaceMip(replacementImage, currentMip);
            refreshMip();
        }

        private void buttonUpMip_Click(object sender, EventArgs e)
        {
            if (mipCount > currentMip + 1)
            {
                currentMip++;
            }
            refreshMip();
        }

        private void refreshMip()
        {
            mipLabel.Text = "Mip " + currentMip + " of " + (internalTexture.mipMaps.Length - 1);
            pictureBox1.Image = internalTexture.getPreviewMip(currentMip);
            pictureBox1.Size = pictureBox1.Image.Size;
            mipCount = internalTexture.mipMaps.Length;
            if (internalTexture is XvrTextureFile && (internalTexture as XvrTextureFile).SaveTextureType != (internalTexture as XvrTextureFile).OriginalTextureType)
            {
                labelTextureFormatValue.Text = (internalTexture as XvrTextureFile).SaveTextureType.ToString();
                labelTextureFormatValue.ForeColor = Color.Green;
            }
            buttonUpMip.Enabled = (mipCount > currentMip + 1);
            buttonDownMip.Enabled = (currentMip > 0);
        }

        private void buttonDownMip_Click(object sender, EventArgs e)
        {
            if (currentMip > 0)
            {
                currentMip--;
            }
            refreshMip();
        }

        private void importImageButton_Click(object sender, EventArgs e)
        {
            if (importTextureDialog.ShowDialog() != DialogResult.OK)
                return;

            // XVR/PVR import path: replace the raw texture binary directly.
            if (importTextureDialog.FileName.EndsWith(".xvr") || importTextureDialog.FilterIndex == 2)
            {
                using (Stream tempStream = importTextureDialog.OpenFile())
                {
                    using (BinaryReader tempFile = new BinaryReader(tempStream))
                    {
                        try
                        {
                            internalTexture.loadXvrFile(tempFile.ReadBytes((int)tempStream.Length));
                            currentMip = 0;
                            refreshMip();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("Could not read the XVR/PVR file:\n" + ex.Message, "Texture Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
                return;
            }

            // PNG (or any other Bitmap-readable) import path.
            byte[] rawBitmap = File.ReadAllBytes(importTextureDialog.FileName);
            Bitmap importedImage = new Bitmap(new MemoryStream(rawBitmap));
            if (!isPowerOfTwo(importedImage.Width) || !isPowerOfTwo(importedImage.Height))
            {
                MessageBox.Show("Texture width and height must each be a power of 2.\nAllowed sizes: 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 1024.", "Image Size Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (importedImage.Width > 1024 || importedImage.Height > 1024)
            {
                if (MessageBox.Show("Textures larger than 1024x1024 generally don't render correctly.\nTextures above 2048x2048 frequently crash the game.\nAre you sure you wish to continue?", "Image Size Warning", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    return;
                }
            }
            if (internalTexture is XvrTextureFile)
            {
                (internalTexture as XvrTextureFile).SaveTextureType = PsuTextureType.Raster;
            }
            internalTexture.loadImage(importedImage, rebuildMipsCheckbox.Checked);
            currentMip = 0;
            refreshMip();
        }

        private bool isPowerOfTwo(int x)
        {
            return x > 0 && (x & (x - 1)) == 0;
        }

        private void buttonExport_Click(object sender, EventArgs e)
        {
            exportTextureDialog.FileName = filename.Replace(".xvr", ".png");
            if (exportTextureDialog.ShowDialog() != DialogResult.OK)
                return;

            string outPath = exportTextureDialog.FileName;
            if (!outPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Only PNG export is currently supported. Please use a .png file extension.", "Unsupported Format", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            internalTexture.mipMaps[0].Save(outPath);
        }

        private void buttonSaveMip_Click(object sender, EventArgs e)
        {
            saveMipDialog.FileName = filename.Replace(".xvr", "") + "_mip_" + currentMip.ToString() + ".png";
            if (saveMipDialog.ShowDialog() != DialogResult.OK)
                return;

            // Bug fix: this used to check exportTextureDialog.FileName (copy/paste error),
            // which meant the Save button silently did nothing if Export had never been opened.
            string outPath = saveMipDialog.FileName;
            if (!outPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Only PNG export is currently supported. Please use a .png file extension.", "Unsupported Format", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            internalTexture.mipMaps[currentMip].Save(outPath);
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (internalTexture is XvrTextureFile)
            {
                ((XvrTextureFile)internalTexture).SaveTextureType = PsuTextureType.Raster;
                PsuTexturePixelFormat fmt;
                Enum.TryParse<PsuTexturePixelFormat>(pixelFormatDropDown.SelectedValue.ToString(), out fmt);
                ((XvrTextureFile)internalTexture).SavePixelFormat = fmt;
                refreshMip();
            }
        }
    }
}