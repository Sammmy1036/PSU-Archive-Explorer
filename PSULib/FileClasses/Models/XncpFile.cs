using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PSULib.FileClasses.General;
using PSULib.Support;

namespace PSULib.FileClasses.Models
{
    /// Parser for XNCP (Ninja CellSpriteDraw Project) UI files
    public class XncpFile : PsuFile
    {
        // ---- Public data model ----
        public string ProjectName { get; private set; }
        public List<XncpScene> Scenes { get; } = new List<XncpScene>();
        public List<XncpFontInfo> Fonts { get; } = new List<XncpFontInfo>();
        public byte[] RawBytes { get; private set; }
        public string ParseError { get; private set; }
        public string ParseInfo { get; private set; }
        private int _ownerOffset;
        private int[] _originalPtrs;
        private int _originalFileOffset;

        // ---- Constructors ----
        public XncpFile(string filename, byte[] rawData, byte[] inHeader, int[] ptrs, int baseAddr)
        {
            this.filename = filename;
            this.header = inHeader;
            _originalPtrs = ptrs;
            _originalFileOffset = baseAddr;
            this.calculatedPointers = null;  // populated lazily in ToRaw()
            RawBytes = rawData;

            try
            {
                byte[] toParse = ReconstructFile(rawData, inHeader);
                if (toParse == null || toParse.Length < 32)
                {
                    ParseError = "Buffer too small or null";
                    return;
                }
                _ownerOffset = toParse.Length - (rawData?.Length ?? 0);
                if (_ownerOffset < 0) _ownerOffset = 0;

                int ncpjPos = FindBytes(toParse, "nCPJ", 0);
                if (ncpjPos < 0)
                {
                    ParseError = "nCPJ chunk not found in buffer";
                    return;
                }
                int rootStringPos = FindCStringInRange(toParse, "Root", ncpjPos, toParse.Length);
                int delta = 0;
                if (rootStringPos >= 0)
                {
                    int storedProjOff = BitConverter.ToInt32(toParse, ncpjPos + 0x14);
                    int expectedAbs = ncpjPos + storedProjOff;
                    delta = expectedAbs - rootStringPos;
                }

                ParseInfo =
                    "toParse: " + toParse.Length + " bytes, " +
                    "nCPJ at 0x" + ncpjPos.ToString("X") + ", " +
                    (rootStringPos >= 0
                        ? ("Root string at 0x" + rootStringPos.ToString("X") +
                           ", delta=0x" + delta.ToString("X"))
                        : "Root string not found - parsing without relocation correction");

                Parse(toParse, ncpjPos, delta);
            }
            catch (Exception ex)
            {
                ParseError = ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace;
            }
        }

        public XncpFile() { }
        public override byte[] ToRaw()
        {
            if (_originalPtrs == null || _originalPtrs.Length == 0
                || RawBytes == null)
            {
                this.calculatedPointers = new int[0];
                return RawBytes;
            }
            byte[] copy = (byte[])RawBytes.Clone();
            var rawRelativePositions = new List<int>(_originalPtrs.Length);
            foreach (int absPos in _originalPtrs)
            {
                int rel = absPos - _originalFileOffset;
                if (rel < 0 || rel + 4 > copy.Length) continue;

                int currentValue = BitConverter.ToInt32(copy, rel);
                int unrelocated = currentValue - _originalFileOffset;
                Buffer.BlockCopy(BitConverter.GetBytes(unrelocated), 0,
                                 copy, rel, 4);
                rawRelativePositions.Add(rel);
            }

            this.calculatedPointers = rawRelativePositions.ToArray();
            return copy;
        }
        public List<string> EditLog { get; } = new List<string>();
        private const int MaxEditLog = 30;

        private void RecordEdit(int pos, byte[] before, byte[] after, string fieldKind)
        {
            string entry = $"0x{pos:X}: {BitConverter.ToString(before)} -> "
                         + $"{BitConverter.ToString(after)} ({fieldKind})";
            EditLog.Add(entry);
            if (EditLog.Count > MaxEditLog) EditLog.RemoveAt(0);
        }

        internal void WriteFloat(int pos, float value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "float=" + value);
            dirty = true;
        }

        internal void WriteInt32(int pos, int value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "int=" + value);
            dirty = true;
        }

        internal void WriteUInt32(int pos, uint value)
        {
            if (RawBytes == null || pos < 0 || pos + 4 > RawBytes.Length) return;
            byte[] before = new byte[4];
            Buffer.BlockCopy(RawBytes, pos, before, 0, 4);
            byte[] bytes = BitConverter.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, RawBytes, pos, 4);
            RecordEdit(pos, before, bytes, "uint=" + value);
            dirty = true;
        }

        // ---- Buffer reconstruction ----
        private static byte[] ReconstructFile(byte[] rawData, byte[] inHeader)
        {
            if (rawData == null) return null;
            if (inHeader == null || inHeader.Length == 0) return rawData;
            string headerSig = inHeader.Length >= 4
                ? Encoding.ASCII.GetString(inHeader, 0, 4) : "";
            if (headerSig == "NXIF" || headerSig == "NYIF")
            {
                byte[] combined = new byte[inHeader.Length + rawData.Length];
                Buffer.BlockCopy(inHeader, 0, combined, 0, inHeader.Length);
                Buffer.BlockCopy(rawData, 0, combined, inHeader.Length, rawData.Length);
                return combined;
            }
            return rawData;
        }

        // ---- Scanning helpers ----
        private static int FindBytes(byte[] buf, string magic, int startAt)
        {
            byte[] needle = Encoding.ASCII.GetBytes(magic);
            for (int i = startAt; i <= buf.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                    if (buf[i + j] != needle[j]) { match = false; break; }
                if (match) return i;
            }
            return -1;
        }

        private static int FindCStringInRange(byte[] buf, string needle, int start, int end)
        {
            byte[] target = Encoding.ASCII.GetBytes(needle);
            int limit = Math.Min(end, buf.Length) - target.Length - 1;
            for (int i = start; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < target.Length; j++)
                    if (buf[i + j] != target[j]) { match = false; break; }
                if (match && buf[i + target.Length] == 0) return i;
            }
            return -1;
        }

        // ---- Parser core ----
        private byte[] data;
        private int gBase;
        private int delta;

        // Shift-JIS is the encoding PSU's name strings use throughout. It's a
        // strict superset of ASCII for byte values 0x00-0x7F, so English names
        // like "window" decode identically. Cached on the type because looking
        // up an Encoding by name is non-trivial and we call ResolveString a lot.
        private static readonly Encoding ShiftJis = Encoding.GetEncoding("shift-jis");

        private string ResolveString(int storedOffset)
        {
            if (storedOffset == 0) return "";
            int abs = (gBase + storedOffset) - delta;
            if (abs < 0 || abs >= data.Length) return "";
            int end = abs;
            while (end < data.Length && data[end] != 0) end++;
            int len = end - abs;
            if (len <= 0 || len > 1024) return "";
            // The previous version tried Encoding.ASCII first and "fell back"
            // to Shift-JIS in a catch block, but Encoding.ASCII.GetString
            // never throws on non-ASCII bytes; it silently replaces them
            // with '?'. That's why Japanese names showed up as "??????".
            // Decoding directly as Shift-JIS handles both ASCII and Japanese.
            return ShiftJis.GetString(data, abs, len);
        }

        private int ResolveOffset(int storedOffset)
        {
            if (storedOffset == 0) return 0;
            int abs = (gBase + storedOffset) - delta;
            if (abs < 0 || abs >= data.Length) return -1;
            return abs;
        }

        private uint U32(int absPos)
        {
            if (absPos < 0 || absPos + 4 > data.Length) return 0;
            return BitConverter.ToUInt32(data, absPos);
        }
        private int S32(int absPos)
        {
            if (absPos < 0 || absPos + 4 > data.Length) return 0;
            return BitConverter.ToInt32(data, absPos);
        }
        private float F32(int absPos)
        {
            if (absPos < 0 || absPos + 4 > data.Length) return 0f;
            return BitConverter.ToSingle(data, absPos);
        }

        private void Parse(byte[] buf, int ncpjPos, int relocationDelta)
        {
            data = buf;
            gBase = ncpjPos;
            delta = relocationDelta;

            int body = ncpjPos + 8;
            int rootOff = (int)U32(body + 0x08);
            int projOff = (int)U32(body + 0x0C);
            int fontOff = (int)U32(body + 0x14);

            ProjectName = ResolveString(projOff);

            int rootAbs = ResolveOffset(rootOff);
            if (rootAbs > 0) ParseCsdNode(rootAbs);

            if (fontOff != 0)
            {
                int fontAbs = ResolveOffset(fontOff);
                if (fontAbs > 0) ParseFontList(fontAbs);
            }
        }

        private void ParseCsdNode(int abs)
        {
            int sceneCount = (int)U32(abs + 0x00);
            int sceneTableOff = (int)U32(abs + 0x04);
            int sceneIdTblOff = (int)U32(abs + 0x08);
            int nodeCount = (int)U32(abs + 0x0C);
            int nodeListOff = (int)U32(abs + 0x10);

            if (sceneCount < 0 || sceneCount > 1000) sceneCount = 0;
            if (nodeCount < 0 || nodeCount > 1000) nodeCount = 0;
            var sceneNames = new string[sceneCount];
            for (int k = 0; k < sceneCount; k++) sceneNames[k] = "";
            if (sceneIdTblOff != 0)
            {
                int tblAbs = ResolveOffset(sceneIdTblOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < sceneCount; i++)
                    {
                        int entry = tblAbs + i * 8;
                        if (entry + 8 > data.Length) break;
                        int nmOff = (int)U32(entry + 0);
                        int sceneIdx = (int)U32(entry + 4);
                        string nm = nmOff != 0 ? ResolveString(nmOff) : "";
                        if (sceneIdx >= 0 && sceneIdx < sceneCount)
                            sceneNames[sceneIdx] = nm;
                    }
                }
            }

            if (sceneTableOff != 0)
            {
                int tblAbs = ResolveOffset(sceneTableOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < sceneCount; i++)
                    {
                        int entry = tblAbs + i * 4;
                        if (entry + 4 > data.Length) break;
                        int sceneOff = (int)U32(entry);
                        if (sceneOff == 0) continue;
                        int sceneAbs = ResolveOffset(sceneOff);
                        if (sceneAbs <= 0) continue;
                        string name = !string.IsNullOrEmpty(sceneNames[i])
                            ? sceneNames[i] : ("scene_" + i);
                        var scene = ParseScene(sceneAbs, name);
                        if (scene != null) Scenes.Add(scene);
                    }
                }
            }
            if (nodeListOff != 0 && nodeCount > 0)
            {
                int tblAbs = ResolveOffset(nodeListOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < nodeCount; i++)
                    {
                        int childAbs = tblAbs + i * 0x18;
                        if (childAbs + 0x18 > data.Length) break;
                        ParseCsdNode(childAbs);
                    }
                }
            }
        }

        private XncpScene ParseScene(int abs, string name)
        {
            if (abs + 0x4C > data.Length) return null;

            var scene = new XncpScene
            {
                Name = name,
            };
            scene._owner = this;
            scene._scenePos = abs;
            scene._ownerOffset = _ownerOffset;
            scene.SetAspectRatioRaw(F32(abs + 0x40));
            scene.SetZIndexRaw(F32(abs + 0x04));
            scene.SetFramerateRaw(F32(abs + 0x08));

            int subImgCount = (int)U32(abs + 0x1C);
            int subImgOff = (int)U32(abs + 0x20);
            int groupCount = (int)U32(abs + 0x24);
            int groupTableOff = (int)U32(abs + 0x28);
            int castCount = (int)U32(abs + 0x2C);
            int castDictOff = (int)U32(abs + 0x30);
            int animCount = (int)U32(abs + 0x34);
            int animDictOff = (int)U32(abs + 0x3C);

            if (groupCount < 0 || groupCount > 1000) groupCount = 0;
            if (castCount < 0 || castCount > 10000) castCount = 0;
            if (animCount < 0 || animCount > 10000) animCount = 0;
            if (subImgCount < 0 || subImgCount > 100000) subImgCount = 0;

            if (subImgOff != 0)
            {
                int tblAbs = ResolveOffset(subImgOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < subImgCount; i++)
                    {
                        int e = tblAbs + i * 0x14;
                        if (e + 0x14 > data.Length) break;
                        scene.SubImages.Add(new XncpSubImage
                        {
                            TextureIndex = (int)U32(e + 0x00),
                            TopLeftX = F32(e + 0x04),
                            TopLeftY = F32(e + 0x08),
                            BottomRightX = F32(e + 0x0C),
                            BottomRightY = F32(e + 0x10),
                        });
                    }
                }
            }

            var castNames = new Dictionary<long, string>();
            if (castDictOff != 0)
            {
                int tblAbs = ResolveOffset(castDictOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < castCount; i++)
                    {
                        int e = tblAbs + i * 0x0C;
                        if (e + 0x0C > data.Length) break;
                        int nmOff = (int)U32(e + 0);
                        long grp = U32(e + 4);
                        long idx = U32(e + 8);
                        long key = (grp << 32) | (idx & 0xFFFFFFFFL);
                        if (!castNames.ContainsKey(key))
                            castNames[key] = nmOff != 0 ? ResolveString(nmOff) : "";
                    }
                }
            }

            if (animDictOff != 0)
            {
                int tblAbs = ResolveOffset(animDictOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < animCount; i++)
                    {
                        int e = tblAbs + i * 8;
                        if (e + 8 > data.Length) break;
                        int nmOff = (int)U32(e + 0);
                        var anim = new XncpAnimation
                        {
                            Name = nmOff != 0 ? ResolveString(nmOff) : "",
                        };
                        anim._owner = this;
                        anim._entryPos = e;
                        anim._ownerOffset = _ownerOffset;
                        anim.SetIndexRaw((int)U32(e + 4));
                        scene.Animations.Add(anim);
                    }
                }
            }

            if (groupTableOff != 0)
            {
                int tblAbs = ResolveOffset(groupTableOff);
                if (tblAbs > 0)
                {
                    for (int g = 0; g < groupCount; g++)
                    {
                        int grpAbs = tblAbs + g * 0x10;
                        if (grpAbs + 0x10 > data.Length) break;
                        var grp = ParseCastGroup(grpAbs, g, castNames);
                        if (grp != null) scene.Groups.Add(grp);
                    }
                }
            }

            return scene;
        }

        private XncpCastGroup ParseCastGroup(int abs, int groupIndex, Dictionary<long, string> castNames)
        {
            int castCount = (int)U32(abs + 0x00);
            int castTblOff = (int)U32(abs + 0x04);

            if (castCount < 0 || castCount > 10000) return null;

            var group = new XncpCastGroup { Index = groupIndex };
            if (castTblOff != 0)
            {
                int tblAbs = ResolveOffset(castTblOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < castCount; i++)
                    {
                        int entry = tblAbs + i * 4;
                        if (entry + 4 > data.Length) break;
                        int dataOff = (int)U32(entry);
                        if (dataOff == 0) continue;
                        int castAbs = ResolveOffset(dataOff);
                        if (castAbs <= 0) continue;

                        var cast = ParseUiCast(castAbs);
                        if (cast == null) continue;
                        cast.GroupIndex = groupIndex;
                        cast.IndexInGroup = i;
                        long key = ((long)groupIndex << 32) | ((long)i & 0xFFFFFFFFL);
                        string nm;
                        if (castNames.TryGetValue(key, out nm)) cast.Name = nm;
                        group.Casts.Add(cast);
                    }
                }
            }
            return group;
        }

        // PSU XNCP cast struct is 0x50 bytes (NOT THE 0x74 FROM TGE's 010)
        // THIS TOOK ME THREE WEEKS LONG TO FIGURE OUT.
        //   +0x00  uint32   Field00          constant 1, sentinel
        //   +0x04  uint32   DrawType         0=None, 1=Sprite, 2=Font
        //   +0x08  uint32   reserved         constant 1 (NOT IsEnabled)
        //   +0x0C..+0x28    UV corners (8 floats)
        //   +0x2C  uint32   sentinel         0x7FFF
        //   +0x30  uint32   CastInfoOff      per-cast pointer
        //   +0x34  uint32   flag/enum        small set of values
        //   +0x38  uint32   shared pointer   constant within a file
        //   +0x3C  uint32   constant         0x20
        //   +0x40  uint32   MaterialInfoOff  per-cast pointer
        //   +0x44  uint32   FontCharsOff     shared pointer (font casts)
        //   +0x48  uint32   FontNameOff      shared pointer (font casts)
        //   +0x4C  uint32   padding          0
        // IsEnabled actually lives in the CastInfo block at +0x00, not in
        // the cast struct. Width/Height/OffsetX/OffsetY/ScaleType/Field68/
        // Field6C/ScaleType2/FontSpacing/Field54 don't exist in PSU's cast
        private XncpCast ParseUiCast(int abs)
        {
            if (abs + 0x50 > data.Length) return null;

            var cast = new XncpCast();
            cast._owner = this;
            cast._castDataPos = abs;
            cast._castInfoPos = -1;
            cast._matInfoPos = -1;
            cast._ownerOffset = _ownerOffset;

            cast.SetDrawTypeRaw((XncpCastType)U32(abs + 0x04));
            // cast +0x00 is a sentinel constant (1); cast +0x08 is reserved
            // (also always 1)

            cast.SetTopLeftXRaw(F32(abs + 0x0C));
            cast.SetTopLeftYRaw(F32(abs + 0x10));
            cast.SetBottomLeftXRaw(F32(abs + 0x14));
            cast.SetBottomLeftYRaw(F32(abs + 0x18));
            cast.SetTopRightXRaw(F32(abs + 0x1C));
            cast.SetTopRightYRaw(F32(abs + 0x20));
            cast.SetBottomRightXRaw(F32(abs + 0x24));
            cast.SetBottomRightYRaw(F32(abs + 0x28));
            cast.SetField34Raw(U32(abs + 0x34));
            cast.SetField3CRaw(U32(abs + 0x3C));

            // FontCharsOff and FontNameOff are at +0x44 and +0x48
            int fontCharsOff = (int)U32(abs + 0x44);
            int fontNameOff = (int)U32(abs + 0x48);
            cast.FontName = fontNameOff != 0 ? ResolveString(fontNameOff) : "";
            cast.FontCharacters = fontCharsOff != 0 ? ResolveString(fontCharsOff) : "";

            int infoOff = (int)U32(abs + 0x30);
            if (infoOff != 0)
            {
                int infoAbs = ResolveOffset(infoOff);
                if (infoAbs > 0 && infoAbs + 0x30 <= data.Length)
                {
                    cast.HasCastInfo = true;
                    cast._castInfoPos = infoAbs;
                    cast.SetEnabledRaw(U32(infoAbs + 0x00) == 0);
                    cast.SetTranslationXRaw(F32(infoAbs + 0x04));
                    cast.SetTranslationYRaw(F32(infoAbs + 0x08));
                    cast.SetRotationRaw(F32(infoAbs + 0x0C));
                    cast.SetScaleXRaw(F32(infoAbs + 0x10));
                    cast.SetScaleYRaw(F32(infoAbs + 0x14));
                    cast.SetColorRaw(U32(infoAbs + 0x1C));
                    cast.SetGradientTLRaw(U32(infoAbs + 0x20));
                    cast.SetGradientBLRaw(U32(infoAbs + 0x24));
                    cast.SetGradientTRRaw(U32(infoAbs + 0x28));
                    cast.SetGradientBRRaw(U32(infoAbs + 0x2C));
                }
            }

            int matOff = (int)U32(abs + 0x40);
            if (matOff != 0)
            {
                int matAbs = ResolveOffset(matOff);
                if (matAbs > 0)
                {
                    cast._matInfoPos = matAbs;
                    cast.SetTextureIndexRaw(S32(matAbs + 0x00));
                }
            }

            return cast;
        }

        private void ParseFontList(int abs)
        {
            if (abs + 12 > data.Length) return;
            int fontCount = (int)U32(abs + 0);
            int tblOff = (int)U32(abs + 4);
            int idTblOff = (int)U32(abs + 8);

            if (fontCount < 0 || fontCount > 1000) return;
            var names = new string[fontCount];
            for (int k = 0; k < fontCount; k++) names[k] = "";
            if (idTblOff != 0)
            {
                int tblAbs = ResolveOffset(idTblOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < fontCount; i++)
                    {
                        int e = tblAbs + i * 8;
                        if (e + 8 > data.Length) break;
                        int nmOff = (int)U32(e + 0);
                        int fontIdx = (int)U32(e + 4);
                        string nm = nmOff != 0 ? ResolveString(nmOff) : "";
                        if (fontIdx >= 0 && fontIdx < fontCount)
                            names[fontIdx] = nm;
                    }
                }
            }
            if (tblOff != 0)
            {
                int tblAbs = ResolveOffset(tblOff);
                if (tblAbs > 0)
                {
                    for (int i = 0; i < fontCount; i++)
                    {
                        int e = tblAbs + i * 8;
                        if (e + 8 > data.Length) break;
                        Fonts.Add(new XncpFontInfo
                        {
                            Name = !string.IsNullOrEmpty(names[i]) ? names[i] : ("font_" + i),
                            CharacterCount = (int)U32(e + 0),
                        });
                    }
                }
            }
        }
    }
    // Data model classes
    public enum XncpCastType { None = 0, Sprite = 1, Font = 2 }

    public class XncpScene
    {
        public string Name { get; set; }
        public List<XncpCastGroup> Groups { get; } = new List<XncpCastGroup>();
        public List<XncpAnimation> Animations { get; } = new List<XncpAnimation>();
        public List<XncpSubImage> SubImages { get; } = new List<XncpSubImage>();
        public int TotalCastCount
        {
            get { int n = 0; foreach (var g in Groups) n += g.Casts.Count; return n; }
        }

        internal XncpFile _owner;
        internal int _scenePos;
        internal int _ownerOffset;

        private float _zIndex;
        private float _framerate;
        private float _aspectRatio;

        public float ZIndex
        {
            get { return _zIndex; }
            set
            {
                _zIndex = value;
                if (_owner != null && _scenePos >= 0)
                    _owner.WriteFloat(_scenePos + 0x04 - _ownerOffset, value);
            }
        }
        public float Framerate
        {
            get { return _framerate; }
            set
            {
                _framerate = value;
                if (_owner != null && _scenePos >= 0)
                    _owner.WriteFloat(_scenePos + 0x08 - _ownerOffset, value);
            }
        }
        public float AspectRatio
        {
            get { return _aspectRatio; }
            set
            {
                _aspectRatio = value;
                if (_owner != null && _scenePos >= 0)
                    _owner.WriteFloat(_scenePos + 0x40 - _ownerOffset, value);
            }
        }

        internal void SetZIndexRaw(float v) { _zIndex = v; }
        internal void SetFramerateRaw(float v) { _framerate = v; }
        internal void SetAspectRatioRaw(float v) { _aspectRatio = v; }
    }

    public class XncpCastGroup
    {
        public int Index { get; set; }
        public List<XncpCast> Casts { get; } = new List<XncpCast>();
    }

    public class XncpCast
    {
        public string Name { get; set; } = "";
        public int GroupIndex { get; set; }
        public int IndexInGroup { get; set; }
        public string FontName { get; set; } = "";
        public string FontCharacters { get; set; } = "";

        internal XncpFile _owner;
        internal int _castDataPos;     // absolute position of cast data in toParse
        internal int _castInfoPos;     // absolute position of CastInfo block (or -1)
        internal int _matInfoPos;      // absolute position of MaterialInfo block (or -1)
        internal int _ownerOffset;     // toParse-pos minus RawBytes-pos

        private XncpCastType _drawType;
        private bool _isEnabled;
        private float _translationX, _translationY;
        private float _rotation;
        private float _scaleX = 1f, _scaleY = 1f;
        private int _textureIndex = -1;
        private uint _color;               // CastInfo +0x1C

        public XncpCastType DrawType
        {
            get { return _drawType; }
            set
            {
                _drawType = value;
                if (_owner != null && _castDataPos >= 0)
                    _owner.WriteUInt32(_castDataPos + 0x04 - _ownerOffset, (uint)value);
            }
        }
        /// <summary>
        /// Whether the cast is rendered. Stored in CastInfo +0x00 with
        /// inverted polarity: byte 0 = visible, byte 1 = hidden.
        ///
        /// The cast struct's +0x08 word is reserved (always 1) and was
        /// misread as IsEnabled in older versions of this parser. Casts
        /// without a CastInfo block can't be toggled.
        /// </summary>
        public bool IsEnabled
        {
            get { return _isEnabled; }
            set
            {
                _isEnabled = value;
                if (_owner != null && _castInfoPos >= 0)
                {
                    // Invert: enabled (visible) = 0, disabled (hidden) = 1.
                    uint raw = value ? 0u : 1u;
                    _owner.WriteUInt32(_castInfoPos + 0x00 - _ownerOffset, raw);
                }
            }
        }
        public float TranslationX
        {
            get { return _translationX; }
            set
            {
                _translationX = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteFloat(_castInfoPos + 0x04 - _ownerOffset, value);
            }
        }
        public float TranslationY
        {
            get { return _translationY; }
            set
            {
                _translationY = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteFloat(_castInfoPos + 0x08 - _ownerOffset, value);
            }
        }
        public float Rotation
        {
            get { return _rotation; }
            set
            {
                _rotation = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteFloat(_castInfoPos + 0x0C - _ownerOffset, value);
            }
        }
        public float ScaleX
        {
            get { return _scaleX; }
            set
            {
                _scaleX = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteFloat(_castInfoPos + 0x10 - _ownerOffset, value);
            }
        }
        public float ScaleY
        {
            get { return _scaleY; }
            set
            {
                _scaleY = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteFloat(_castInfoPos + 0x14 - _ownerOffset, value);
            }
        }
        public int TextureIndex
        {
            get { return _textureIndex; }
            set
            {
                _textureIndex = value;
                if (_owner != null && _matInfoPos >= 0)
                    _owner.WriteInt32(_matInfoPos + 0x00 - _ownerOffset, value);
            }
        }

        /// <summary>
        /// CastInfo +0x1C. Primary tint color in inverted RGBA8 format.
        /// Lives in the optional CastInfo block, so the setter is a no-op
        /// when HasCastInfo is false.
        /// </summary>
        public uint Color
        {
            get { return _color; }
            set
            {
                _color = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteUInt32(_castInfoPos + 0x1C - _ownerOffset, value);
            }
        }

        // ---- Backing fields for the per-cast / per-CastInfo extras ----
        // Field34 / Field3C are real cast bytes and I couldn't figure out what they do
        private uint _field34;
        private uint _field3C;
        private uint _gradTL;
        private uint _gradBL;
        private uint _gradTR;
        private uint _gradBR;

        // Field34 lives in the cast struct at +0x34 and looks like an enum/flag
        // again no idea what it does, but I left it editable if someone wants to experiment
        public uint Field34
        {
            get { return _field34; }
            set
            {
                _field34 = value;
                if (_owner != null && _castDataPos >= 0)
                    _owner.WriteUInt32(_castDataPos + 0x34 - _ownerOffset, value);
            }
        }
        // Field3C at cast +0x3C is almost always 0x20. Probably a constant sentinel?
        public uint Field3C
        {
            get { return _field3C; }
            set
            {
                _field3C = value;
                if (_owner != null && _castDataPos >= 0)
                    _owner.WriteUInt32(_castDataPos + 0x3C - _ownerOffset, value);
            }
        }
        public uint GradientTL
        {
            get { return _gradTL; }
            set
            {
                _gradTL = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteUInt32(_castInfoPos + 0x20 - _ownerOffset, value);
            }
        }
        public uint GradientBL
        {
            get { return _gradBL; }
            set
            {
                _gradBL = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteUInt32(_castInfoPos + 0x24 - _ownerOffset, value);
            }
        }
        public uint GradientTR
        {
            get { return _gradTR; }
            set
            {
                _gradTR = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteUInt32(_castInfoPos + 0x28 - _ownerOffset, value);
            }
        }
        public uint GradientBR
        {
            get { return _gradBR; }
            set
            {
                _gradBR = value;
                if (_owner != null && _castInfoPos >= 0)
                    _owner.WriteUInt32(_castInfoPos + 0x2C - _ownerOffset, value);
            }
        }

        // UV corners. The four (X, Y) pairs are how the engine knows which
        // part of the texture atlas to sample. Layout in the cast struct:
        //   +0x0C / +0x10  TopLeftX / TopLeftY
        //   +0x14 / +0x18  BottomLeftX / BottomLeftY
        //   +0x1C / +0x20  TopRightX / TopRightY
        //   +0x24 / +0x28  BottomRightX / BottomRightY
        private float _tlX, _tlY, _blX, _blY, _trX, _trY, _brX, _brY;

        public float TopLeftX
        {
            get { return _tlX; }
            set
            {
                _tlX = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x0C - _ownerOffset, value);
            }
        }
        public float TopLeftY
        {
            get { return _tlY; }
            set
            {
                _tlY = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x10 - _ownerOffset, value);
            }
        }
        public float BottomLeftX
        {
            get { return _blX; }
            set
            {
                _blX = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x14 - _ownerOffset, value);
            }
        }
        public float BottomLeftY
        {
            get { return _blY; }
            set
            {
                _blY = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x18 - _ownerOffset, value);
            }
        }
        public float TopRightX
        {
            get { return _trX; }
            set
            {
                _trX = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x1C - _ownerOffset, value);
            }
        }
        public float TopRightY
        {
            get { return _trY; }
            set
            {
                _trY = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x20 - _ownerOffset, value);
            }
        }
        public float BottomRightX
        {
            get { return _brX; }
            set
            {
                _brX = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x24 - _ownerOffset, value);
            }
        }
        public float BottomRightY
        {
            get { return _brY; }
            set
            {
                _brY = value; if (_owner != null && _castDataPos >= 0)
                    _owner.WriteFloat(_castDataPos + 0x28 - _ownerOffset, value);
            }
        }

        internal void SetTopLeftXRaw(float v) { _tlX = v; }
        internal void SetTopLeftYRaw(float v) { _tlY = v; }
        internal void SetBottomLeftXRaw(float v) { _blX = v; }
        internal void SetBottomLeftYRaw(float v) { _blY = v; }
        internal void SetTopRightXRaw(float v) { _trX = v; }
        internal void SetTopRightYRaw(float v) { _trY = v; }
        internal void SetBottomRightXRaw(float v) { _brX = v; }
        internal void SetBottomRightYRaw(float v) { _brY = v; }

        public bool HasCastInfo { get; set; }

        internal void SetDrawTypeRaw(XncpCastType v) { _drawType = v; }
        internal void SetEnabledRaw(bool v) { _isEnabled = v; }
        internal void SetTranslationXRaw(float v) { _translationX = v; }
        internal void SetTranslationYRaw(float v) { _translationY = v; }
        internal void SetRotationRaw(float v) { _rotation = v; }
        internal void SetScaleXRaw(float v) { _scaleX = v; }
        internal void SetScaleYRaw(float v) { _scaleY = v; }
        internal void SetTextureIndexRaw(int v) { _textureIndex = v; }
        internal void SetColorRaw(uint v) { _color = v; }

        internal void SetField34Raw(uint v) { _field34 = v; }
        internal void SetField3CRaw(uint v) { _field3C = v; }
        internal void SetGradientTLRaw(uint v) { _gradTL = v; }
        internal void SetGradientBLRaw(uint v) { _gradBL = v; }
        internal void SetGradientTRRaw(uint v) { _gradTR = v; }
        internal void SetGradientBRRaw(uint v) { _gradBR = v; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Name)
                ? ("cast_" + GroupIndex + "_" + IndexInGroup)
                : Name;
        }
    }

    public class XncpAnimation
    {
        internal XncpFile _owner;
        internal int _entryPos = -1;
        internal int _ownerOffset;

        public string Name { get; set; }

        private int _index;
        public int Index
        {
            get { return _index; }
            set
            {
                _index = value;
                if (_owner != null && _entryPos >= 0)
                    _owner.WriteInt32(_entryPos + 4 - _ownerOffset, value);
            }
        }

        internal void SetIndexRaw(int v) { _index = v; }

        public override string ToString() { return Name ?? ("anim_" + _index); }
    }

    public class XncpSubImage
    {
        public int TextureIndex { get; set; }
        public float TopLeftX, TopLeftY, BottomRightX, BottomRightY;
    }

    public class XncpFontInfo
    {
        public string Name { get; set; }
        public int CharacterCount { get; set; }
        public override string ToString() { return Name + " (" + CharacterCount + " chars)"; }
    }
}