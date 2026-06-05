using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using PSULib.FileClasses.Models;

namespace psu_archive_explorer.FileViewers
{
    /// <summary>
    /// Viewer/editor for PSU RIPC face-feature texture files.
    ///
    /// Layout mirrors the XvrViewer:
    ///   Top info row       — Pixel Format | Texture Format | Image Size
    ///   Left group box     — "Current View": scrollable PictureBox + zoom trackbar
    ///                        groupBoxMip is anchored Top|Left|Right so it fills horizontally
    ///   Right group box    — "Entire Texture": Import / Export / Copy / Export Palette
    ///   Bottom group box   — "Colour Palette": only the PaletteSize real entries shown
    ///
    /// Key design decisions:
    ///   - ripc.Palette (Color[]) is always used for palette reads/writes, never
    ///     TextureBitmap.Palette, because GDI+ zeroes alpha on indexed bitmap palettes.
    ///   - Only ripc.PaletteSize entries are drawn; unused (black) slots are not shown.
    ///   - ACT export writes exactly 768 bytes (256 × RGB) — the strict standard size
    ///     recognised by Photoshop, GIMP and Aseprite.
    /// </summary>
    public partial class RipcFileViewer : UserControl
    {
        public RipcFile LoadedFile { get; private set; }

        private const int PALETTE_COLS = 16;
        private const int SWATCH_SIZE = 18;
        private int _hoveredPaletteIdx = -1;

        private float _zoom = 4f;
        private const float ZOOM_MIN = 1f;
        private const float ZOOM_MAX = 12f;

        // -----------------------------------------------------------------------
        // Constructor
        // -----------------------------------------------------------------------

        public RipcFileViewer(RipcFile ripc)
        {
            InitializeComponent();
            LoadedFile = ripc;
            SetupTooltips();
            Populate();
            // Defer ResizePaletteBox until after the control is fully laid out
            // so groupBoxTexture.Left has its correct runtime value.
            this.HandleCreated += (s, e) => BeginInvoke(new Action(ResizePaletteBox));
        }

        // -----------------------------------------------------------------------
        // Population
        // -----------------------------------------------------------------------

        private void Populate()
        {
            var ripc = LoadedFile;
            if (ripc == null) { ShowFallback("No file loaded.", null); return; }
            if (!string.IsNullOrEmpty(ripc.ParseError))
            { ShowFallback(ripc.ParseError, ripc.ToRaw()); return; }
            if (ripc.TextureBitmap == null)
            { ShowFallback(ripc.ParseWarning ?? "Texture could not be decoded.", ripc.ToRaw()); return; }

            labelPixelFormatValue.Text = ripc.ImageDepth + "bpp indexed";
            labelTextureFormatValue.Text = GuessFeatureType(ripc.filename ?? "");
            labelImageSizeValue.Text = ripc.ImageWidth + "×" + ripc.ImageHeight + " px";

            zoomTrackBar.Value = (int)_zoom;
            zoomValueLabel.Text = _zoom + "×";

            RebuildActiveEntries();
            RefreshPictureBox();
            palettePanel.Invalidate();
        }

        // -----------------------------------------------------------------------
        // Tooltips
        // -----------------------------------------------------------------------

        private void SetupTooltips()
        {
            var tip = new ToolTip { AutoPopDelay = 10000, InitialDelay = 400, ReshowDelay = 200, ShowAlways = true };
            tip.SetToolTip(buttonImport, "Replace the ENTIRE texture from a PNG or BMP. Must match the original width × height.");
            tip.SetToolTip(buttonExport, "Export the full texture to PNG or BMP.");
            tip.SetToolTip(buttonCopy, "Copy the current texture to the clipboard.");
            tip.SetToolTip(buttonExportPal, "Export the colour palette as an Adobe Colour Table (.act) — 768 bytes, compatible with Photoshop/GIMP/Aseprite.");
            tip.SetToolTip(zoomTrackBar, "Zoom level for the texture preview (1× – 12×).");
        }

        // -----------------------------------------------------------------------
        // PictureBox refresh
        // -----------------------------------------------------------------------

        private void RefreshPictureBox()
        {
            var ripc = LoadedFile;
            if (ripc?.TextureBitmap == null) return;

            var src = ripc.TextureBitmap;
            int dw = Math.Max(1, (int)(src.Width * _zoom));
            int dh = Math.Max(1, (int)(src.Height * _zoom));

            var scaled = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(scaled))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
                g.DrawImage(src, 0, 0, dw, dh);
            }
            pictureBox1.Image = scaled;
            pictureBox1.Size = scaled.Size;
        }

        // -----------------------------------------------------------------------
        // Zoom
        // -----------------------------------------------------------------------

        private void zoomTrackBar_Scroll(object sender, EventArgs e)
        {
            _zoom = Math.Max(ZOOM_MIN, Math.Min(ZOOM_MAX, zoomTrackBar.Value));
            zoomValueLabel.Text = _zoom + "×";
            RefreshPictureBox();
        }

        // -----------------------------------------------------------------------
        // Palette helpers
        //
        // RIPC palettes store 256 entries but most are zero (ARGB=0x00000000).
        // The file header's PaletteSize is often 256 even for sparse palettes,
        // and real colour entries can appear anywhere — not just at the start.
        //
        // Strategy: collect ONLY the non-zero entries into a compact list that
        // records each entry's original index.  The grid displays only those.
        // Clicks map back to the original index to patch ripc.Palette correctly.
        // -----------------------------------------------------------------------

        // Compact entry: display-slot colour + original palette index.
        private struct PaletteEntry { public Color Colour; public int OriginalIndex; }

        // Cached compact list — rebuilt whenever the file changes.
        private PaletteEntry[] _activeEntries = new PaletteEntry[0];

        /// <summary>
        /// Rebuild _activeEntries from ripc.Palette, keeping only non-zero entries.
        /// Call after loading a file or after any palette edit.
        /// </summary>
        private void RebuildActiveEntries()
        {
            var ripc = LoadedFile;
            if (ripc?.Palette == null) { _activeEntries = new PaletteEntry[0]; ResizePaletteBox(); return; }

            int ceiling = ripc.Palette.Length;

            var list = new System.Collections.Generic.List<PaletteEntry>();
            for (int i = 0; i < ceiling; i++)
            {
                Color c = ripc.Palette[i];
                if (c.R == 0 && c.G == 0 && c.B == 0) continue;
                list.Add(new PaletteEntry { Colour = c, OriginalIndex = i });
            }
            _activeEntries = list.ToArray();
            ResizePaletteBox();
        }

        /// <summary>
        /// Shrink groupBoxPalette (and the UserControl if needed) to exactly the
        /// height required for the active swatch rows — no wasted black space.
        /// </summary>
        private void ResizePaletteBox()
        {
            const int TOP = 4;
            const int V_PAD = 4;
            const int GAP = 4;

            int rows = (_activeEntries.Length + PALETTE_COLS - 1) / PALETTE_COLS;
            if (rows < 1) rows = 1;

            int panelH = TOP + rows * SWATCH_SIZE + V_PAD;
            int labelH = labelPaletteTitle.Height;
            int bottom = this.ClientSize.Height - 4;

            // Cap groupBoxMip width so it never overlaps the button column
            int maxWidth = groupBoxTexture.Left - groupBoxMip.Left - 8;
            if (groupBoxMip.Width > maxWidth)
                groupBoxMip.Width = maxWidth;

            // Close the gap: stretch groupBoxMip height down to the palette label
            int mipBottom = bottom - panelH - labelH - GAP;
            if (mipBottom > groupBoxMip.Top)
                groupBoxMip.Height = mipBottom - groupBoxMip.Top;

            // Palette label and panel match groupBoxMip left/width
            labelPaletteTitle.Width = groupBoxMip.Width;
            labelPaletteTitle.Location = new System.Drawing.Point(groupBoxMip.Left, bottom - panelH - labelH);

            palettePanel.Size = new System.Drawing.Size(groupBoxMip.Width, panelH);
            palettePanel.Location = new System.Drawing.Point(groupBoxMip.Left, bottom - panelH);
        }

        // -----------------------------------------------------------------------
        // Palette paint — renders only the compact non-zero entries.
        // -----------------------------------------------------------------------

        private void palettePanel_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(System.Drawing.SystemColors.Control);

            if (_activeEntries == null || _activeEntries.Length == 0) return;

            using (var labelFont = new Font(FontFamily.GenericSansSerif, 6.5f))
            using (var hoverPen = new Pen(Color.White, 1.5f))
            using (var activePen = new Pen(Color.Yellow, 1f))
            {
                const int TOP = 4;

                for (int slot = 0; slot < _activeEntries.Length; slot++)
                {
                    Color c = _activeEntries[slot].Colour;
                    int col = slot % PALETTE_COLS;
                    int row = slot / PALETTE_COLS;
                    int x = 4 + col * SWATCH_SIZE;
                    int y = TOP + row * SWATCH_SIZE;

                    // Checkerboard for semi-transparent entries
                    if (c.A < 255)
                    {
                        for (int cy = 0; cy < SWATCH_SIZE; cy++)
                            for (int cx = 0; cx < SWATCH_SIZE; cx++)
                                e.Graphics.FillRectangle(
                                    ((cx + cy) % 2 == 0) ? Brushes.LightGray : Brushes.White,
                                    x + cx, y + cy, 1, 1);
                    }

                    using (var brush = new SolidBrush(c))
                        e.Graphics.FillRectangle(brush, x, y, SWATCH_SIZE, SWATCH_SIZE);

                    // Yellow outline on visually non-black entries
                    if (c.R > 0 || c.G > 0 || c.B > 0 || (c.A > 0 && c.A < 255))
                        e.Graphics.DrawRectangle(activePen, x, y, SWATCH_SIZE - 1, SWATCH_SIZE - 1);

                    // White hover outline — also makes black swatches discoverable
                    if (slot == _hoveredPaletteIdx)
                        e.Graphics.DrawRectangle(hoverPen, x, y, SWATCH_SIZE - 1, SWATCH_SIZE - 1);
                }

                // (hover info is shown in labelPaletteTitle, not drawn here)
            }
        }

        // -----------------------------------------------------------------------
        // Palette mouse
        // -----------------------------------------------------------------------

        private void palettePanel_MouseMove(object sender, MouseEventArgs e)
        {
            int slot = HitTestSwatch(e.X, e.Y);
            if (slot != _hoveredPaletteIdx)
            {
                _hoveredPaletteIdx = slot;
                palettePanel.Invalidate();
            }

            if (slot >= 0 && slot < _activeEntries.Length)
            {
                var entry = _activeEntries[slot];
                Color c = entry.Colour;
                labelPaletteTitle.Text = string.Format(
                    "Palette[{0}]:  R={1}  G={2}  B={3}  A={4}   #{5:X2}{6:X2}{7:X2}  — click to edit",
                    entry.OriginalIndex, c.R, c.G, c.B, c.A, c.R, c.G, c.B);
            }
            else
            {
                labelPaletteTitle.Text = "Colour Palette  (click a swatch to recolour)";
            }
        }

        private void palettePanel_MouseLeave(object sender, EventArgs e)
        {
            _hoveredPaletteIdx = -1;
            labelPaletteTitle.Text = "Colour Palette  (click a swatch to recolour)";
            palettePanel.Invalidate();
        }

        private void palettePanel_MouseClick(object sender, MouseEventArgs e)
        {
            var ripc = LoadedFile;
            if (ripc?.Palette == null || ripc.TextureBitmap == null) return;

            int slot = HitTestSwatch(e.X, e.Y);
            if (slot < 0 || slot >= _activeEntries.Length) return;

            int originalIdx = _activeEntries[slot].OriginalIndex;
            Color current = _activeEntries[slot].Colour;

            using (var dlg = new ColorDialog { Color = current, FullOpen = true, AnyColor = true })
            {
                if (ShowColorDialogCentered(dlg) != DialogResult.OK) return;

                // Patch the authoritative Color[] at the original index
                ripc.Palette[originalIdx] = dlg.Color;

                // Keep the bitmap palette in sync
                var bmpPal = ripc.TextureBitmap.Palette;
                if (originalIdx < bmpPal.Entries.Length)
                {
                    bmpPal.Entries[originalIdx] = dlg.Color;
                    ripc.TextureBitmap.Palette = bmpPal;
                }

                RebuildActiveEntries();
                palettePanel.Invalidate();
                RefreshPictureBox();
            }
        }

        /// <summary>Returns display slot index under (x,y), or -1 if outside grid.</summary>
        private int HitTestSwatch(int x, int y)
        {
            const int TOP = 4;
            int col = (x - 4) / SWATCH_SIZE;
            int row = (y - TOP) / SWATCH_SIZE;
            if (col < 0 || col >= PALETTE_COLS || row < 0) return -1;
            int slot = row * PALETTE_COLS + col;
            return (slot >= 0 && slot < _activeEntries.Length) ? slot : -1;
        }

        // -----------------------------------------------------------------------
        // Export texture (PNG or BMP)
        // -----------------------------------------------------------------------

        private void buttonExport_Click(object sender, EventArgs e)
        {
            var ripc = LoadedFile;
            if (ripc?.TextureBitmap == null) return;

            exportTextureDialog.FileName =
                Path.GetFileNameWithoutExtension(ripc.filename ?? "ripc_export");
            if (exportTextureDialog.ShowDialog() != DialogResult.OK) return;

            string path = exportTextureDialog.FileName;
            try
            {
                var argb = ConvertToArgb(ripc.TextureBitmap);
                if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                    argb.Save(path, ImageFormat.Bmp);
                else
                    argb.Save(path, ImageFormat.Png);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Export failed:\n" + ex.Message, "Export Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Import replacement texture
        // -----------------------------------------------------------------------

        private void buttonImport_Click(object sender, EventArgs e)
        {
            var ripc = LoadedFile;
            if (ripc?.TextureBitmap == null) return;

            if (importTextureDialog.ShowDialog() != DialogResult.OK) return;

            Bitmap imported;
            try { imported = new Bitmap(importTextureDialog.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show("Could not read the selected image:\n" + ex.Message,
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (imported.Width != ripc.ImageWidth || imported.Height != ripc.ImageHeight)
            {
                MessageBox.Show(
                    string.Format(
                        "The replacement image must be exactly {0}×{1} px to match the original.\n" +
                        "The selected file is {2}×{3} px.",
                        ripc.ImageWidth, ripc.ImageHeight, imported.Width, imported.Height),
                    "Size Mismatch", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var quantised = QuantiseToIndexed(imported, ripc.Palette);
                ReplaceTextureBitmap(ripc, quantised);
                RefreshPictureBox();
                palettePanel.Invalidate();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not import the image:\n" + ex.Message,
                    "Import Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Export palette — strict 768-byte ACT format
        //
        // Adobe Colour Table (.act) is defined as exactly 256 × 3 RGB bytes = 768 bytes.
        // Any trailing footer (e.g. 772-byte variant) is not universally supported and
        // causes "invalid file" errors in some apps. We pad unused entries with black.
        // -----------------------------------------------------------------------

        private void buttonExportPal_Click(object sender, EventArgs e)
        {
            var ripc = LoadedFile;
            if (ripc == null) return;

            if (_activeEntries == null || _activeEntries.Length == 0)
            {
                MessageBox.Show("No palette entries to export.", "Nothing to Export",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            exportPaletteDialog.FileName =
                Path.GetFileNameWithoutExtension(ripc.filename ?? "ripc_palette") + ".act";
            if (exportPaletteDialog.ShowDialog() != DialogResult.OK) return;

            // Strict ACT: exactly 768 bytes, 256 RGB triples, unused entries = black.
            var bytes = new byte[768];   // zero-initialised = black for unused entries
            for (int slot = 0; slot < _activeEntries.Length && slot < 256; slot++)
            {
                Color c = _activeEntries[slot].Colour;
                bytes[slot * 3] = c.R;
                bytes[slot * 3 + 1] = c.G;
                bytes[slot * 3 + 2] = c.B;
            }

            try { File.WriteAllBytes(exportPaletteDialog.FileName, bytes); }
            catch (Exception ex)
            {
                MessageBox.Show("Palette export failed:\n" + ex.Message, "Export Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // -----------------------------------------------------------------------
        // Copy to clipboard
        // -----------------------------------------------------------------------

        private void buttonCopy_Click(object sender, EventArgs e)
        {
            var ripc = LoadedFile;
            if (ripc?.TextureBitmap == null) return;
            Clipboard.SetImage(ConvertToArgb(ripc.TextureBitmap));
        }

        // -----------------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------------

        private static Bitmap ConvertToArgb(Bitmap src)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dst))
                g.DrawImage(src, 0, 0);
            return dst;
        }

        private static Bitmap QuantiseToIndexed(Bitmap src, Color[] palette)
        {
            var dst = new Bitmap(src.Width, src.Height, PixelFormat.Format8bppIndexed);
            var dstPal = dst.Palette;
            for (int i = 0; i < palette.Length && i < dstPal.Entries.Length; i++)
                dstPal.Entries[i] = palette[i];
            dst.Palette = dstPal;

            BitmapData bd = dst.LockBits(
                new Rectangle(0, 0, dst.Width, dst.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);

            var rowBuf = new byte[bd.Stride];
            for (int y = 0; y < src.Height; y++)
            {
                for (int x = 0; x < src.Width; x++)
                    rowBuf[x] = (byte)FindNearestPaletteIndex(src.GetPixel(x, y), palette);

                Marshal.Copy(rowBuf, 0,
                    (IntPtr)(bd.Scan0.ToInt64() + (long)y * bd.Stride),
                    bd.Stride);
            }
            dst.UnlockBits(bd);
            return dst;
        }

        private static int FindNearestPaletteIndex(Color c, Color[] entries)
        {
            int best = 0;
            long bestD = long.MaxValue;
            for (int i = 0; i < entries.Length; i++)
            {
                long dr = c.R - entries[i].R;
                long dg = c.G - entries[i].G;
                long db = c.B - entries[i].B;
                long da = c.A - entries[i].A;
                long d = dr * dr + dg * dg + db * db + da * da;
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        private static void ReplaceTextureBitmap(RipcFile ripc, Bitmap newBitmap)
        {
            var prop = typeof(RipcFile).GetProperty("TextureBitmap",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (prop != null && prop.CanWrite) { prop.SetValue(ripc, newBitmap); return; }

            var field = typeof(RipcFile).GetField("<TextureBitmap>k__BackingField",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null) { field.SetValue(ripc, newBitmap); return; }

            throw new InvalidOperationException(
                "Cannot replace TextureBitmap on RipcFile. " +
                "Add a public setter or ReplaceTexture(Bitmap) to the class.");
        }

        // -----------------------------------------------------------------------
        // Centered ColorDialog helper
        //
        // Windows common dialogs ignore the owner window for positioning and
        // always open at the screen center.  We work around this by starting a
        // one-shot Timer before ShowDialog(); the timer fires on the UI thread,
        // finds the dialog window by class name, and moves it over our form.
        // -----------------------------------------------------------------------

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern System.IntPtr FindWindow(string lpClassName, string lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(System.IntPtr hWnd, System.IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetWindowRect(System.IntPtr hWnd, out System.Drawing.Rectangle lpRect);

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        private DialogResult ShowColorDialogCentered(ColorDialog dlg)
        {
            Form owner = this.FindForm();

            // Fire once on the UI thread shortly after ShowDialog blocks
            var timer = new System.Windows.Forms.Timer { Interval = 10 };
            timer.Tick += (s, e) =>
            {
                timer.Stop();

                // The full ColorDialog class is "#32770" (common dialog)
                System.IntPtr hDlg = FindWindow("#32770", null);
                if (hDlg == System.IntPtr.Zero) return;

                System.Drawing.Rectangle dlgRect;
                if (!GetWindowRect(hDlg, out dlgRect)) return;

                int dlgW = dlgRect.Width - dlgRect.X;
                int dlgH = dlgRect.Height - dlgRect.Y;

                // Centre over the owner form
                System.Drawing.Rectangle ownerBounds = (owner != null)
                    ? owner.Bounds
                    : System.Windows.Forms.Screen.PrimaryScreen.Bounds;

                int x = ownerBounds.Left + (ownerBounds.Width - dlgW) / 2;
                int y = ownerBounds.Top + (ownerBounds.Height - dlgH) / 2;

                SetWindowPos(hDlg, System.IntPtr.Zero, x, y, 0, 0,
                    SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            };
            timer.Start();

            return dlg.ShowDialog(owner);
        }

        // -----------------------------------------------------------------------
        // Feature type guesser
        // -----------------------------------------------------------------------

        private static string GuessFeatureType(string filename)
        {
            string f = filename.ToLowerInvariant();
            if (f.Contains("eye") && !f.Contains("brow") && !f.Contains("lash")) return "Eye iris";
            if (f.Contains("eyebrow") || f.Contains("brow")) return "Eyebrow";
            if (f.Contains("eyelash") || f.Contains("lash")) return "Eyelash";
            if (f.Contains("lip")) return "Lip";
            if (f.Contains("skin") || f.Contains("face")) return "Skin / face";
            if (f.Contains("xnf") || f.Contains("xng")) return "Face preset (RIPC)";
            return "Face feature";
        }

        // -----------------------------------------------------------------------
        // Fallback hex-dump
        // -----------------------------------------------------------------------

        private void ShowFallback(string reason, byte[] raw)
        {
            labelPixelFormatValue.Text = "-";
            labelTextureFormatValue.Text = "-";
            labelImageSizeValue.Text = "-";

            var hexBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new Font(FontFamily.GenericMonospace, 8.5f),
                WordWrap = false,
                BackColor = Color.White,
                Text = (raw == null || raw.Length == 0)
                                ? "No raw bytes.\r\nReason: " + reason
                                : "Reason: " + reason + "\r\n" + new string('-', 50) + "\r\n"
                                  + BuildHexDump(raw)
            };

            scrollPanel.Controls.Clear();
            scrollPanel.Controls.Add(hexBox);
        }

        private static string BuildHexDump(byte[] data)
        {
            const int bpr = 16, max = 64 * 1024;
            int count = Math.Min(data.Length, max);
            var sb = new StringBuilder(count * 4);
            for (int rs = 0; rs < count; rs += bpr)
            {
                sb.Append(rs.ToString("X8")).Append("  ");
                for (int col = 0; col < bpr; col++)
                {
                    int idx = rs + col;
                    sb.Append(idx < count ? data[idx].ToString("X2") + " " : "   ");
                    if (col == 7) sb.Append(' ');
                }
                sb.Append(' ');
                for (int col = 0; col < bpr; col++)
                {
                    int idx = rs + col;
                    if (idx >= count) break;
                    byte b = data[idx];
                    sb.Append(b >= 0x20 && b < 0x7F ? (char)b : '.');
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }
    }
}