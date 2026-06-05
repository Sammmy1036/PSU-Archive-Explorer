using PSULib.FileClasses.General;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PSULib.FileClasses.Models
{
    /// <summary>
    /// Parsed RIPC face-feature texture file.
    ///
    /// RIPC is a paletted (8bpp indexed) texture used by PSU for face
    /// customisation — eye irises, eyebrows, eyelashes, skin tones etc.
    ///
    /// Layout:
    ///   [0x00] "RIPC" magic (4)
    ///   [0x04] unk_a          (4)
    ///   [0x08] file_chunks    (4)  bit0 = has image section
    ///   [0x0c] file_size      (4)
    ///   [0x10] unk_b          (2)
    ///   [0x12] palette_size   (2)  number of palette entries (usually 256)
    ///   [0x14] unk_c          (2)
    ///   [0x16] palette_depth  (2)  bits per palette entry (32 = ARGB)
    ///   [0x18] img_hdr_offset (4)  offset to image header (from file start)
    ///   [0x1c] unk_d          (4)
    ///   [0x20] palette data        palette_size * 4 bytes (ARGB little-endian)
    ///   [img_hdr_offset+16]        image header:
    ///            unk_e(2) width(2) height(2) depth(2) length(4) unk_f(4)
    ///   [img_hdr_offset+28]        raw indexed pixel data (length-16 bytes)
    /// </summary>
    public class RipcFile : PsuFile
    {
        /// <summary>The decoded texture bitmap ready to display, or null on failure.</summary>
        public Bitmap TextureBitmap { get; private set; }

        public int ImageWidth { get; private set; }
        public int ImageHeight { get; private set; }
        public int ImageDepth { get; private set; }   // bits per pixel (4 or 8)
        public int PaletteSize { get; private set; }   // number of palette entries

        /// <summary>ARGB palette entries decoded from the file.</summary>
        public Color[] Palette { get; private set; }

        public string ParseError { get; private set; }
        public string ParseWarning { get; private set; }

        private byte[] _raw;

        // -- Constructors --------------------------------------------------------

        public RipcFile() { }

        public RipcFile(byte[] rawData, string inFilename = null)
        {
            filename = inFilename;
            _raw = rawData;

            if (rawData == null || rawData.Length < 32)
            {
                ParseError = "rawData too short to be a RIPC file.";
                return;
            }

            string magic = Encoding.ASCII.GetString(rawData, 0, 4);
            if (magic != "RIPC")
            {
                ParseError = "Not a RIPC file (magic: " + magic + ")";
                return;
            }

            try { Parse(rawData); }
            catch (Exception ex) { ParseError = ex.Message; }
        }

        // -- Parser --------------------------------------------------------------

        private void Parse(byte[] d)
        {
            int fileChunks = BitConverter.ToInt32(d, 8);
            int paletteSize = BitConverter.ToUInt16(d, 0x12);
            int imgHdrOffset = BitConverter.ToInt32(d, 0x18);

            PaletteSize = paletteSize;

            // Read palette (starts at 0x20, each entry = 4 bytes ARGB LE)
            Palette = new Color[Math.Max(paletteSize, 256)];
            int palPos = 0x20;
            for (int i = 0; i < paletteSize && palPos + 4 <= d.Length; i++, palPos += 4)
            {
                // Stored as little-endian ARGB int: bytes = [B, G, R, A]
                byte b = d[palPos];
                byte g = d[palPos + 1];
                byte r = d[palPos + 2];
                byte a = d[palPos + 3];
                Palette[i] = Color.FromArgb(a, r, g, b);
            }

            // Read image header and pixel data
            // Image header is at imgHdrOffset + 16 (the +16 skips an outer block)
            int ihPos = imgHdrOffset + 16;
            if ((fileChunks & 1) == 0 || ihPos + 12 > d.Length)
            {
                ParseWarning = "No image section present in this RIPC file.";
                return;
            }

            int width = BitConverter.ToInt16(d, ihPos + 2);
            int height = BitConverter.ToInt16(d, ihPos + 4);
            int depth = BitConverter.ToInt16(d, ihPos + 6);
            int length = BitConverter.ToInt32(d, ihPos + 8);
            int pixPos = ihPos + 12;
            int pixLen = length - 16;   // matches PalTextureFile: reads length-16 bytes

            if (width <= 0 || height <= 0 || pixLen <= 0 || pixPos + pixLen > d.Length)
            {
                ParseWarning = string.Format(
                    "Image dimensions invalid or pixel data out of range " +
                    "({0}x{1} depth={2} length={3}).", width, height, depth, length);
                return;
            }

            ImageWidth = width;
            ImageHeight = height;
            ImageDepth = depth;

            // Build RGBA bitmap from indexed pixels
            PixelFormat fmt = (depth == 4)
                ? PixelFormat.Format4bppIndexed
                : PixelFormat.Format8bppIndexed;

            var bmp = new Bitmap(width, height, fmt);

            // Apply palette to bitmap
            var bmpPal = bmp.Palette;
            for (int i = 0; i < Palette.Length && i < bmpPal.Entries.Length; i++)
                bmpPal.Entries[i] = Palette[i];
            bmp.Palette = bmpPal;

            // Copy pixel bytes
            BitmapData bd = bmp.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly, fmt);
            int copyLen = Math.Min(pixLen, bd.Stride * height);
            Marshal.Copy(d, pixPos, bd.Scan0, copyLen);
            bmp.UnlockBits(bd);

            TextureBitmap = bmp;
        }

        public override byte[] ToRaw() { return _raw; }
    }
}