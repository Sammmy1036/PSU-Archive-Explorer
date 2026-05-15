using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PSULib.FileClasses.General;

namespace PSULib.FileClasses.Models
{
    /// <summary>
    /// Parser for PSU .xnj (NN-format joint/skeleton) files.
    ///
    /// XNJ is part of Sonic Team's "Ninja Next" (NN) format family. The NN
    /// format originated on the Sega Saturn and was carried forward through
    /// Sonic the Hedgehog, Phantasy Star Universe, and other 2000s-era Sega
    /// games. PSU's XNJ files contain the skeleton: per-bone hierarchy,
    /// translation, rotation, and scale in bind pose.
    ///
    /// IMPORTANT: this is unrelated to <see cref="XnrFile"/> despite the
    /// similar name. XnrFile handles NXR-magic'd UI-parameter files (flat
    /// float-pair arrays). XNJ uses the NN chunked-container format
    /// (NXIF + NXNN + NXOB chunks). The two formats share three letters of
    /// extension and absolutely nothing else.
    ///
    /// DEPENDENCY NOTE: this class deliberately exposes plain float fields
    /// rather than System.Numerics.Vector3 / Quaternion / Matrix4x4. PSULib
    /// is kept free of the System.Numerics dependency to match how XnrFile
    /// and NomFile are written (bare floats / float lists). Consumers that
    /// want math types — e.g. the GLB exporter in the main app project —
    /// convert these primitives into Vector3/Quaternion on their side, where
    /// System.Numerics is already available.
    ///
    /// File layout:
    ///   File header                — "XNJ\0" magic, internal filename string
    ///   NXIF chunk                 — file pointers/sizes
    ///   NXOB chunk                 — header + bone records
    ///   Each bone record = 0x90 (144) bytes laid out as NN_NODE struct.
    ///
    /// Per-bone record layout (offsets relative to record start):
    ///   +0x00 uint32   type            (rotation order / behavior flags)
    ///   +0x04 int16    weightUsed      (vertex-weight slot id; -1 if unused)
    ///   +0x06 int16    parentIndex     (-1 if root)
    ///   +0x08 int16    firstChildIndex (-1 if leaf)
    ///   +0x0A int16    nextSiblingIndex(-1 if last sibling)
    ///   +0x0C..0x17    NODE_TRN — local translation (3 floats: X, Y, Z)
    ///   +0x18..0x23    NODE_ROT — local rotation in BAMS (3 int32s).
    ///                  BAMS = Binary Angular Measurement System: a full
    ///                  rotation is 0x10000 (65536). Degrees = int * 360/65536.
    ///                  Rotation order is XZY per Sonic Team convention
    ///                  (NND_NODETYPE_ROTATE_TYPE_XZY in the original SDK).
    ///   +0x24..0x2F    NODE_SCL — local scale (3 floats: X, Y, Z)
    ///   +0x30..0x6F    NODE_INVINIT_MTX — inverse bind matrix (16 floats,
    ///                  row-major). Used for skinning; we parse it for
    ///                  completeness but pure animation export doesn't need it.
    ///   +0x70..0x8F    NODE_CENTER, NODE_RADIUS, bounds — we don't use these.
    ///
    /// Format knowledge here is derived from Shadowth117's PSO2-Aqua-Library
    /// (GPL-3.0). This is a clean-room reimplementation of the same format
    /// spec — no source code is copied. Original NN-format research credit:
    /// Agrajag, per the comment header in that project's NNObject.cs.
    /// </summary>
    public class XnjFile : PsuFile
    {
        /// <summary>The parsed bone array, in file order. For standard PSU
        /// character XNJs this order matches <c>NomFile.boneNames</c>.</summary>
        public List<XnjBone> Bones { get; } = new List<XnjBone>();

        /// <summary>Set if parsing failed; null if successful. Caller can
        /// surface this to the user without throwing.</summary>
        public string ParseError { get; private set; }

        private const int BoneRecordSize = 0x90;

        // Conversion from BAMS (Binary Angular Measurement System) integer to
        // radians: BAMS uses a 16.16-ish fixed-point where 0x10000 = 360
        // degrees. Values are stored as int32 even though only the low 16
        // bits carry the angle for a single rotation; the full int32 range
        // allows multi-turn animation values.
        private const double BamsToRadians = 2.0 * Math.PI / 65536.0;
        private const double BamsToDegrees = 360.0 / 65536.0;

        public XnjFile(string filename, byte[] rawData, byte[] inHeader, int[] ptrs, int baseAddr)
        {
            this.filename = filename;
            this.header = inHeader;

            try
            {
                Parse(rawData, inHeader);
            }
            catch (Exception ex)
            {
                ParseError = ex.GetType().Name + ": " + ex.Message;
            }
        }

        /// <summary>Empty constructor for serialization paths that need one.</summary>
        public XnjFile() { }

        /// <summary>
        /// Walks the file, locates the NXOB chunk, and parses every bone
        /// record it finds there.
        ///
        /// Robustness change: rather than trusting that the first bone record
        /// sits at a fixed nxobPos + 0x20, we scan forward from the chunk for
        /// the first offset that passes LooksLikeBoneRecord. Some XNJ variants
        /// (PSU's map/scene XNJs in particular) have a slightly different NXOB
        /// chunk header length, which pushed the first record off the assumed
        /// 0x20 and caused a hard "No bone records recognized" failure. The
        /// scan keeps the common case identical while tolerating the variants.
        /// </summary>
        private void Parse(byte[] rawData, byte[] inHeader)
        {
            byte[] buf = ReconstructFile(rawData, inHeader);
            reconstructedRaw = buf;   // cache for ToRaw() / hex viewer
            if (buf == null || buf.Length < 0x100)
            {
                ParseError = "Buffer too small to contain XNJ header";
                return;
            }

            // Find the NXOB chunk. The XNJ container has a small variable-
            // length header followed by NXIF, then NXOB. We scan for the
            // NXOB magic rather than trusting fixed offsets — keeps us
            // robust against minor variant headers.
            int nxobPos = FindBytes(buf, NXOB_MAGIC, 0);
            if (nxobPos < 0)
            {
                ParseError = "NXOB chunk not found";
                return;
            }

            // The first bone record is *normally* 0x20 bytes past the NXOB
            // magic — the bytes between are the chunk header (magic + size +
            // header pointer + bone count + fields we don't need). But the
            // chunk header length varies between XNJ variants, so instead of
            // trusting that, scan a small window after the chunk for the
            // first offset that looks like a bone record.
            //
            // We only accept a candidate if the record *after* it also looks
            // valid — a single lone match can be a false positive on
            // arbitrary data, but two 0x90-aligned records in a row is a
            // strong signal we found the real array. The one exception is a
            // genuine single-bone skeleton, where there's no room for a
            // second record; there we accept the lone match.
            const int scanStart = 0x10;   // earliest plausible record offset
            const int scanEnd = 0x80;     // latest plausible record offset
            int firstBoneOffset = -1;

            for (int probe = nxobPos + scanStart; probe <= nxobPos + scanEnd; probe++)
            {
                if (probe + BoneRecordSize > buf.Length)
                    break;
                if (!LooksLikeBoneRecord(buf, probe))
                    continue;

                int next = probe + BoneRecordSize;
                bool nextOk = next + BoneRecordSize > buf.Length
                              || LooksLikeBoneRecord(buf, next);
                if (nextOk)
                {
                    firstBoneOffset = probe;
                    break;
                }
            }

            if (firstBoneOffset < 0)
            {
                ParseError = "No bone records recognized in NXOB chunk "
                    + "(scanned 0x" + (nxobPos + scanStart).ToString("X")
                    + "..0x" + (nxobPos + scanEnd).ToString("X") + ")";
                return;
            }

            // Walk the bone array. Each record is exactly 0x90 bytes and
            // starts with a recognizable type byte sequence. We stop when
            // either the buffer runs out or the type-word signature breaks.
            int pos = firstBoneOffset;
            while (pos + BoneRecordSize <= buf.Length)
            {
                if (!LooksLikeBoneRecord(buf, pos)) break;
                Bones.Add(ParseBoneRecord(buf, pos, Bones.Count));
                pos += BoneRecordSize;
            }

            if (Bones.Count == 0)
            {
                ParseError = "No bone records recognized at offset 0x" + firstBoneOffset.ToString("X");
            }
        }

        /// <summary>
        /// Recognizable bone-record fingerprint: byte 1 is 0x01 (rotation type
        /// constant), and byte 0 is one of the observed flag values. We've
        /// seen 0x84/0x85/0x86 for normal bones and 0xCF for root nodes. The
        /// "size" byte at +0x02 can be 0x1C or 0x00 (terminal bones such as
        /// the weapon attach points have 0x00), so we don't require a
        /// specific value there.
        /// </summary>
        /// 

        private byte[] reconstructedRaw;

        private static bool LooksLikeBoneRecord(byte[] buf, int pos)
        {
            if (pos + 4 > buf.Length) return false;
            byte b0 = buf[pos];
            return (b0 == 0x84 || b0 == 0x85 || b0 == 0x86 || b0 == 0xCF)
                && buf[pos + 1] == 0x01;
        }

        /// <summary>
        /// Decode a single 144-byte bone record into an XnjBone. Field layout
        /// follows the NN_NODE struct as documented in the class header.
        /// </summary>
        private static XnjBone ParseBoneRecord(byte[] buf, int pos, int index)
        {
            var b = new XnjBone();
            b.Index = index;

            // Hierarchy fields. Note that previous-sibling is NOT stored in
            // the file; the "weightUsed" field at +0x04 is for vertex skinning
            // and unrelated to traversal order.
            b.WeightUsed = BitConverter.ToInt16(buf, pos + 0x04);
            b.ParentIndex = BitConverter.ToInt16(buf, pos + 0x06);
            b.FirstChildIndex = BitConverter.ToInt16(buf, pos + 0x08);
            b.NextSiblingIndex = BitConverter.ToInt16(buf, pos + 0x0A);

            // Local translation: 3 floats starting at +0x0C.
            b.LocalTranslationX = BitConverter.ToSingle(buf, pos + 0x0C);
            b.LocalTranslationY = BitConverter.ToSingle(buf, pos + 0x10);
            b.LocalTranslationZ = BitConverter.ToSingle(buf, pos + 0x14);

            // Local rotation: 3 BAMS int32s at +0x18, in XZY rotation order.
            // We expose the rotation both as raw radians per axis AND as
            // degrees, leaving quaternion composition to the consumer (so we
            // don't need System.Numerics here). The XZY order — rotate X,
            // then Z, then Y — must be applied by whoever builds the
            // quaternion; see the comment on the RotationOrder property.
            int rxBams = BitConverter.ToInt32(buf, pos + 0x18);
            int ryBams = BitConverter.ToInt32(buf, pos + 0x1C);
            int rzBams = BitConverter.ToInt32(buf, pos + 0x20);

            b.LocalRotationRadX = (float)(rxBams * BamsToRadians);
            b.LocalRotationRadY = (float)(ryBams * BamsToRadians);
            b.LocalRotationRadZ = (float)(rzBams * BamsToRadians);
            b.LocalRotationDegX = (float)(rxBams * BamsToDegrees);
            b.LocalRotationDegY = (float)(ryBams * BamsToDegrees);
            b.LocalRotationDegZ = (float)(rzBams * BamsToDegrees);

            // Local scale: 3 floats at +0x24. PSU rest poses observed so far
            // are always unit-scale, but we read it anyway in case some bones
            // do use scaling.
            b.LocalScaleX = BitConverter.ToSingle(buf, pos + 0x24);
            b.LocalScaleY = BitConverter.ToSingle(buf, pos + 0x28);
            b.LocalScaleZ = BitConverter.ToSingle(buf, pos + 0x2C);

            // Inverse bind matrix (16 floats at +0x30, row-major). We parse it
            // because consumers that do vertex skinning will need it, but for
            // pure animation export it can be ignored — glTF computes inverse-
            // binds from the rest-pose hierarchy at write time. Stored as a
            // flat 16-element float[] in row-major order.
            b.InverseBindMatrix = new float[16];
            for (int m = 0; m < 16; m++)
            {
                b.InverseBindMatrix[m] = BitConverter.ToSingle(buf, pos + 0x30 + m * 4);
            }

            return b;
        }

        // ---- Buffer reconstruction (mirrors XnrFile's pattern) ----
        // Some load paths give us just the rawData (post-header), others give
        // us rawData + a small inHeader byte[] holding the NXIF preamble.

        private static readonly byte[] NXOB_MAGIC = Encoding.ASCII.GetBytes("NXOB");

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

        private static int FindBytes(byte[] buf, byte[] needle, int startAt)
        {
            for (int i = startAt; i <= buf.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (buf[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public override byte[] ToRaw()
        {
            // XNJ is parse-only — we don't rewrite the format — so "to raw"
            // returns the original file bytes unchanged. This round-trips
            // losslessly (same bytes in, same bytes out) and gives the hex
            // viewer something real to display. Falls back to the base
            // PsuFile header if Parse never ran or the buffer wasn't built.
            if (reconstructedRaw != null)
                return reconstructedRaw;
            return header;
        }
    }

    /// <summary>
    /// One bone in an XNJ skeleton. Transform components are exposed as plain
    /// float fields (rather than System.Numerics types) so PSULib stays free
    /// of that dependency — consistent with how XnrFile and NomFile expose
    /// their data. Consumers that want Vector3 / Quaternion compose them on
    /// their side.
    ///
    /// Rotation is exposed three ways for caller convenience:
    ///   - LocalRotationRad{X,Y,Z} — radians per axis
    ///   - LocalRotationDeg{X,Y,Z} — degrees per axis
    /// In BOTH cases the values are the per-axis Euler angles and the caller
    /// is responsible for composing them in XZY order (rotate around X first,
    /// then Z, then Y). See <see cref="RotationOrderNote"/>.
    /// </summary>
    public class XnjBone
    {
        /// <summary>Bone's index in the parent XnjFile.Bones list.</summary>
        public int Index { get; set; }

        /// <summary>Vertex-weight slot id (the "weightUsed" field). -1 means
        /// this bone doesn't participate in skinning. Not needed for pure
        /// animation export but preserved from the file.</summary>
        public short WeightUsed { get; set; }

        /// <summary>Parent's index, or -1 if this bone is a root. Note PSU
        /// skeletons can legitimately have multiple bones with ParentIndex
        /// = -1 (Body, weapon attach points, etc.).</summary>
        public int ParentIndex { get; set; }

        /// <summary>First child's index, or -1 for leaf bones.</summary>
        public int FirstChildIndex { get; set; }

        /// <summary>Next sibling's index in the parent's child chain, or -1
        /// for the last sibling.</summary>
        public int NextSiblingIndex { get; set; }

        // ---- Local translation (relative to parent) ----
        public float LocalTranslationX { get; set; }
        public float LocalTranslationY { get; set; }
        public float LocalTranslationZ { get; set; }

        // ---- Local rotation, per-axis Euler, XZY order ----
        // Radians form:
        public float LocalRotationRadX { get; set; }
        public float LocalRotationRadY { get; set; }
        public float LocalRotationRadZ { get; set; }
        // Degrees form (same angles, just pre-converted for convenience):
        public float LocalRotationDegX { get; set; }
        public float LocalRotationDegY { get; set; }
        public float LocalRotationDegZ { get; set; }

        // ---- Local scale ----
        public float LocalScaleX { get; set; } = 1.0f;
        public float LocalScaleY { get; set; } = 1.0f;
        public float LocalScaleZ { get; set; } = 1.0f;

        /// <summary>Inverse bind matrix for skinning (the NODE_INVINIT_MTX
        /// field). 16 floats, row-major. Pure-animation exports don't need
        /// this; consumers that want to do vertex skinning do. May be null
        /// if the bone was constructed without parsing (default ctor path).</summary>
        public float[] InverseBindMatrix { get; set; }

        /// <summary>
        /// Documentation marker, not data: the per-axis rotation values on
        /// this class must be composed in XZY order — rotate around the X
        /// axis first, then Z, then Y. This is the Sonic Team NN convention
        /// (NND_NODETYPE_ROTATE_TYPE_XZY in the original SDK enum). Composing
        /// in the wrong order will produce a skeleton that looks plausible at
        /// rest but twists incorrectly under animation.
        /// </summary>
        public const string RotationOrderNote = "XZY";
    }
}