using System;
using System.Collections.Generic;
using System.IO;

namespace PSULib.FileClasses.Characters
{
    /// <summary>
    /// Serializer for <see cref="NomFile"/> — the missing inverse of the
    /// constructor + <c>ReadNomList</c>.
    ///
    /// Strategy: TEMPLATE REWRITE. We never build a NOM from nothing, because
    /// the bytes at 0x0–0x5 and 0xC–0x3F have not been reverse-engineered (the
    /// constructor calls them "meta data we don't understand" and "redundant"
    /// and skips them). Instead we start from the original file buffer
    /// (<c>fileContents</c>) and rewrite ONLY:
    ///
    ///   • 0x6–0x7  frameCount   (must track the new animation length)
    ///   • 0x8–0xB  frameRate    (normally left as-is; rewritten if changed)
    ///   • 0x40 .. 0x200  the four 28-entry Int32 offset tables
    ///   • 0x200 .. EOF   the frame-list blob region
    ///
    /// Everything before 0x40 except frameCount/frameRate is copied verbatim
    /// from the template, so the un-understood header survives untouched.
    ///
    /// Layout facts, confirmed against the reader:
    ///   • Offsets in the tables are ABSOLUTE from the start of the file
    ///     (ReadNomList seeks them with SeekOrigin.Begin).
    ///   • Table order is rotation, X-position, Y-position, Z-position;
    ///     28 Int32 each; region is 0x40 .. 0x200 (0x1C0 bytes).
    ///   • A table entry of 0 means "this bone has no list" — preserved.
    ///   • Each frame is [frame:byte][type:byte] followed by N Int16 of data,
    ///     where N depends on type. A list ends with a terminator frame whose
    ///     frame number == frameCount.
    ///
    /// The terminator type byte:
    ///   • A terminator is identified by the reader SOLELY by its frame number
    ///     equalling frameCount — but the reader still decodes data shorts
    ///     according to the type byte. So a synthesized terminator must use a
    ///     zero-data type or the reader will read into the next blob.
    ///   • Rotation: the reader checks low-nibble (type2) == 0x8 and sizes data
    ///     from the HIGH nibble directly. We synthesize 0x88 — high nibble 0x8
    ///     (zero data), low nibble 0x8 (satisfies the check).
    ///   • Position: the reader subtracts 0x2 from the HIGH nibble before
    ///     sizing. High nibble 0xA → 0x8 (zero data). We synthesize 0xA0.
    /// When the template already had a list for a bone we reuse ITS original
    /// terminator frame verbatim — type byte AND any data shorts it carried —
    /// since a real terminator may legitimately carry data (a rotation
    /// terminator stored as type 0x0 carries a full 4-short quaternion).
    /// </summary>
    public static class NomFileSerializer
    {
        private const int FrameCountOffset = 0x6;
        private const int FrameRateOffset = 0x8;
        private const int TableRegionStart = 0x40;
        private const int BonesPerTable = 28;
        private const int TableRegionEnd = TableRegionStart + 4 * BonesPerTable * 4; // 0x200
        private const int MaxFrameNumber = 255; // NomFrame.frame is a byte

        /// <summary>
        /// Encodes which of the four frame lists a blob belongs to, so the
        /// writer knows which exponent bias and which type table to use.
        /// </summary>
        private enum ListKind { Rotation, PositionX, PositionY, PositionZ }

        /// <summary>
        /// Produce the raw bytes for a NOM, given the template it was loaded
        /// from and the (possibly edited) frame lists / counts on <paramref
        /// name="nom"/>.
        ///
        /// This does NOT mutate <paramref name="nom"/> or the template buffer.
        /// Returns a fresh byte[].
        /// </summary>
        /// <param name="nom">The NomFile whose current frame lists, frameCount
        ///   and frameRate should be written out.</param>
        /// <param name="templateBytes">The original NOM file bytes to use as
        ///   the header/structure template. Must be the bytes the NomFile was
        ///   loaded from (or another NOM for the same skeleton).</param>
        public static byte[] Serialize(NomFile nom, byte[] templateBytes)
        {
            if (nom == null) throw new ArgumentNullException(nameof(nom));
            if (templateBytes == null) throw new ArgumentNullException(nameof(templateBytes));
            if (templateBytes.Length < TableRegionEnd)
                throw new InvalidDataException(
                    "Template NOM is too small to contain the offset tables (" +
                    templateBytes.Length + " bytes, need at least " + TableRegionEnd + ").");

            ValidateLists(nom);
            ValidateFrameNumbers(nom);

            // Header region (0x0 .. 0x40) is copied verbatim from the template,
            // then frameCount and frameRate are patched in place.
            byte[] head = new byte[TableRegionStart];
            Array.Copy(templateBytes, 0, head, 0, TableRegionStart);
            WriteUInt16(head, FrameCountOffset, nom.frameCount);
            WriteSingle(head, FrameRateOffset, nom.frameRate);

            // Build the four offset tables (filled in after blobs are laid out)
            // and the blob region. Blobs start immediately after the tables.
            int[] rotOffsets = new int[BonesPerTable];
            int[] xOffsets = new int[BonesPerTable];
            int[] yOffsets = new int[BonesPerTable];
            int[] zOffsets = new int[BonesPerTable];

            using (var blobStream = new MemoryStream())
            using (var blobWriter = new BinaryWriter(blobStream))
            {
                // Blob offsets are absolute file offsets; the blob region
                // begins at TableRegionEnd.
                WriteAllBlobs(nom.rotationFrameList, ListKind.Rotation, rotOffsets, blobWriter, nom.frameCount);
                WriteAllBlobs(nom.xPositionFrameList, ListKind.PositionX, xOffsets, blobWriter, nom.frameCount);
                WriteAllBlobs(nom.yPositionFrameList, ListKind.PositionY, yOffsets, blobWriter, nom.frameCount);
                WriteAllBlobs(nom.zPositionFrameList, ListKind.PositionZ, zOffsets, blobWriter, nom.frameCount);

                byte[] blobs = blobStream.ToArray();

                // Assemble the final file: head + tables + blobs.
                using (var outStream = new MemoryStream())
                using (var outWriter = new BinaryWriter(outStream))
                {
                    outWriter.Write(head);                       // 0x0 .. 0x40
                    WriteTable(outWriter, rotOffsets);            // rotation
                    WriteTable(outWriter, xOffsets);              // X position
                    WriteTable(outWriter, yOffsets);              // Y position
                    WriteTable(outWriter, zOffsets);              // Z position
                    // sanity: we should now be exactly at TableRegionEnd
                    if (outStream.Position != TableRegionEnd)
                        throw new InvalidOperationException(
                            "Offset table region ended at 0x" +
                            outStream.Position.ToString("X") + ", expected 0x" +
                            TableRegionEnd.ToString("X") + ".");
                    outWriter.Write(blobs);                      // 0x200 .. EOF
                    return outStream.ToArray();
                }
            }
        }

        // ---- list / frame validation -------------------------------------

        private static void ValidateLists(NomFile nom)
        {
            CheckListLength(nom.rotationFrameList, "rotationFrameList");
            CheckListLength(nom.xPositionFrameList, "xPositionFrameList");
            CheckListLength(nom.yPositionFrameList, "yPositionFrameList");
            CheckListLength(nom.zPositionFrameList, "zPositionFrameList");
        }

        private static void CheckListLength(List<List<NomFile.NomFrame>> list, string name)
        {
            if (list == null)
                throw new InvalidDataException(name + " is null.");
            if (list.Count != BonesPerTable)
                throw new InvalidDataException(
                    name + " has " + list.Count + " bones, expected " +
                    BonesPerTable + ". The offset tables are fixed at " +
                    BonesPerTable + " entries.");
        }

        /// <summary>
        /// Enforces the hard format limit: frame numbers are stored in a single
        /// byte, so nothing can exceed 255. The importer should catch this
        /// earlier with a friendly message, but we re-check here so the
        /// serializer can never silently emit a corrupt file.
        /// </summary>
        private static void ValidateFrameNumbers(NomFile nom)
        {
            if (nom.frameCount > MaxFrameNumber)
                throw new InvalidDataException(
                    "frameCount is " + nom.frameCount + " but the NOM format " +
                    "stores frame numbers in one byte (max " + MaxFrameNumber +
                    "). The animation is too long.");

            CheckFrameNumbers(nom.rotationFrameList, "rotation");
            CheckFrameNumbers(nom.xPositionFrameList, "X-position");
            CheckFrameNumbers(nom.yPositionFrameList, "Y-position");
            CheckFrameNumbers(nom.zPositionFrameList, "Z-position");
        }

        private static void CheckFrameNumbers(List<List<NomFile.NomFrame>> lists, string label)
        {
            for (int b = 0; b < lists.Count; b++)
            {
                var frames = lists[b];
                if (frames == null) continue;
                foreach (var f in frames)
                {
                    if (f.frame > MaxFrameNumber)
                        throw new InvalidDataException(
                            label + " bone " + b + " has a frame numbered " +
                            f.frame + ", exceeding the byte maximum of " +
                            MaxFrameNumber + ".");
                }
            }
        }

        // ---- blob writing -------------------------------------------------

        /// <summary>
        /// Writes every bone's blob for one of the four lists, recording the
        /// absolute start offset of each blob into <paramref name="offsets"/>.
        /// A null bone list is left as offset 0 (the "no list" sentinel).
        /// </summary>
        private static void WriteAllBlobs(
            List<List<NomFile.NomFrame>> lists,
            ListKind kind,
            int[] offsets,
            BinaryWriter blobWriter,
            ushort frameCount)
        {
            for (int bone = 0; bone < BonesPerTable; bone++)
            {
                List<NomFile.NomFrame> frames = bone < lists.Count ? lists[bone] : null;

                if (frames == null || frames.Count == 0)
                {
                    offsets[bone] = 0; // sentinel: bone has no list
                    continue;
                }

                // Absolute file offset = table region end + current blob cursor.
                offsets[bone] = TableRegionEnd + (int)blobWriter.BaseStream.Position;
                WriteOneBlob(frames, kind, blobWriter, frameCount);
            }
        }

        /// <summary>
        /// Writes a single bone's frame list, including its terminator frame.
        /// </summary>
        private static void WriteOneBlob(
            List<NomFile.NomFrame> frames,
            ListKind kind,
            BinaryWriter w,
            ushort frameCount)
        {
            bool isRotation = kind == ListKind.Rotation;

            // Locate any existing terminator in the source list (a frame whose
            // number == frameCount). The reader stops at it; we re-emit one but
            // prefer to reuse the template terminator's type byte if present.
            NomFile.NomFrame existingTerminator = null;

            foreach (var f in frames)
            {
                if (f.frame == frameCount)
                {
                    existingTerminator = f;
                    continue; // don't write the terminator inline; it goes last
                }
                WriteFrame(f, isRotation, w);
            }

            WriteTerminator(existingTerminator, isRotation, frameCount, w);
        }

        /// <summary>
        /// Writes one real (non-terminator) frame: the frame byte, the type
        /// byte, then the data shorts. The number of shorts is derived from the
        /// type exactly as the reader's switch expects, so writer and reader
        /// agree on framing.
        /// </summary>
        private static void WriteFrame(NomFile.NomFrame f, bool isRotation, BinaryWriter w)
        {
            // Reconstruct the raw type byte. NomFrame splits it on read into
            // type (high nibble) and type2 (low nibble); the raw byte is
            // type * 0x10 + type2.
            int rawTypeByte = ((f.type & 0x0F) << 4) | (f.type2 & 0x0F);

            int expectedShorts = DataShortsForType(f.type, isRotation);

            // The data we write comes from f.rawData when available (exact
            // original shorts — lossless), falling back to encoding f.data
            // through NomValueCodec when rawData is absent (e.g. frames the
            // importer synthesized from a GLB).
            short[] shorts = ResolveShorts(f, isRotation, expectedShorts);

            w.Write(f.frame);
            w.Write((byte)rawTypeByte);
            for (int i = 0; i < shorts.Length; i++)
                w.Write(shorts[i]);
        }

        /// <summary>
        /// Picks the shorts to write for a frame. Prefers the untouched
        /// original rawData (perfect round-trip for unedited frames); otherwise
        /// encodes the float data via NomValueCodec. Pads or trims to the
        /// exact count the type requires so framing stays correct.
        /// </summary>
        private static short[] ResolveShorts(NomFile.NomFrame f, bool isRotation, int expectedShorts)
        {
            short[] result = new short[expectedShorts];

            bool useRaw = f.rawData != null && f.rawData.Count == expectedShorts;

            for (int i = 0; i < expectedShorts; i++)
            {
                if (useRaw)
                {
                    result[i] = f.rawData[i];
                }
                else if (f.data != null && i < f.data.Count)
                {
                    result[i] = NomValueCodec.Encode(f.data[i], isRotation);
                }
                else
                {
                    result[i] = 0; // missing component — degenerate input, but
                                   // don't desync the stream
                }
            }
            return result;
        }

        /// <summary>
        /// Writes the terminator frame that ends a bone's list. If the template
        /// supplied one we reuse its exact type byte (and any data it carried);
        /// otherwise we synthesize the minimal zero-data terminator the reader
        /// accepts.
        /// </summary>
        private static void WriteTerminator(
            NomFile.NomFrame existing,
            bool isRotation,
            ushort frameCount,
            BinaryWriter w)
        {
            byte frameByte = (byte)(frameCount & 0xFF);

            if (existing != null)
            {
                // Reuse the original terminator verbatim.
                int rawTypeByte = ((existing.type & 0x0F) << 4) | (existing.type2 & 0x0F);
                w.Write(frameByte);
                w.Write((byte)rawTypeByte);
                if (existing.rawData != null)
                    foreach (short s in existing.rawData)
                        w.Write(s);
                return;
            }

            // Synthesize. The reader treats a frame as a terminator purely by
            // its frame number == frameCount; it still decodes data shorts
            // according to the type byte, so the terminator must use a
            // ZERO-DATA type or the reader will consume bytes from the next
            // blob.
            //
            // Rotation: the reader checks low-nibble (type2) == 0x8 and uses
            //   the HIGH nibble directly to size the data. High nibble must be
            //   a zero-data type (0x8–0xB). We emit 0x88: high 0x8 (hold/reset,
            //   0 shorts) + low 0x8 (satisfies the type2 check).
            // Position: the reader subtracts 0x2 from the HIGH nibble before
            //   sizing. High nibble 0xA → 0x8 (zero data). We emit 0xA0.
            byte synthType = isRotation ? (byte)0x88 : (byte)0xA0;
            w.Write(frameByte);
            w.Write(synthType);
        }

        /// <summary>
        /// The number of Int16 data values a frame of the given type carries.
        /// Mirrors the reader's switch in <c>ReadNomList</c> exactly. <paramref
        /// name="type"/> is the HIGH-nibble type value as stored on NomFrame.
        /// </summary>
        private static int DataShortsForType(byte type, bool isRotation)
        {
            if (isRotation)
            {
                switch (type)
                {
                    case 0x0: return 4;                 // full quaternion
                    case 0x5: case 0x6: case 0x7: return 2; // partial quaternion
                    case 0x8: case 0x9: case 0xA: case 0xB: return 0; // hold/reset
                    default:
                        throw new InvalidDataException(
                            "Unknown rotation frame type 0x" + type.ToString("X") +
                            "; serializer cannot determine its data length.");
                }
            }
            else
            {
                switch (type)
                {
                    case 0x0: return 1;                 // direct value
                    case 0x2: return 4;                 // 4-short key
                    case 0x4: return 3;                 // interpolated
                    case 0x6: return 3;                 // 3-short key
                    case 0x8: case 0xA: return 0;       // hold/reset
                    default:
                        throw new InvalidDataException(
                            "Unknown position frame type 0x" + type.ToString("X") +
                            "; serializer cannot determine its data length.");
                }
            }
        }

        // ---- low-level writes --------------------------------------------

        private static void WriteTable(BinaryWriter w, int[] offsets)
        {
            for (int i = 0; i < BonesPerTable; i++)
                w.Write(offsets[i]);
        }

        private static void WriteUInt16(byte[] buf, int offset, ushort value)
        {
            buf[offset] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private static void WriteSingle(byte[] buf, int offset, float value)
        {
            byte[] b = BitConverter.GetBytes(value); // little-endian on x86/x64
            Array.Copy(b, 0, buf, offset, 4);
        }
    }
}