using System;
using System.Collections.Generic;
using System.IO;

namespace psu_archive_explorer
{
    /// <summary>
    /// Sofdec SFD (MPEG-1 System Stream) muxer.
    /// This is the inverse of SofdecDemuxer: it takes a raw MPEG-1 video
    /// elementary stream and a complete ADX audio file and interleaves them
    /// into a valid Sofdec .sfd container.
    ///
    /// Container format (MPEG-1 System Stream):
    ///   Pack header      00 00 01 BA  (MPEG-1 style, 8 bytes after start code)
    ///   Video PES        00 00 01 E0  stream id 0xE0
    ///   Audio PES        00 00 01 BD  private stream 1, substream id 0x40
    ///   End code         00 00 01 B9
    ///
    /// Interleaving strategy: one audio PES packet is written for every video
    /// PES packet, sized to carry the audio data that corresponds to one video
    /// frame's worth of time. This matches the pattern seen in PSU's own SFDs
    /// and keeps A/V sync tightly coupled throughout the file.
    /// </summary>
    internal static class SofdecMuxer
    {
        // Maximum PES payload size (fits in a 16-bit PES length field alongside
        // the minimal PES header). Using 2016 bytes keeps packets small and
        // gives the game's Sofdec reader short seek distances.
        private const int MaxVideoPesPayload = 2016;

        // Audio substream id used by PSU. Must be in the range 0x40-0x5F for
        // the demuxer to recognise it as ADX private stream data.
        private const byte AudioSubstreamId = 0x40;

        // ADX block layout (must match AdxDecoder / AdxEncoder constants).
        private const int AdxBlockSize = 18;
        private const int AdxSamplesPerBlock = 32;

        /// <summary>
        /// Mux a raw MPEG-1 video ES and a complete ADX audio file into SFD bytes.
        /// </summary>
        /// <param name="videoEs">
        ///     Raw MPEG-1 video elementary stream (output of ffmpeg -c:v mpeg1video).
        /// </param>
        /// <param name="adxBytes">
        ///     Complete ADX file including the 0x80 0x00 header
        ///     (output of AdxEncoder.EncodeFromWav).
        /// </param>
        /// <param name="framerate">
        ///     Video framerate in frames per second. Used to calculate how many
        ///     ADX audio blocks belong alongside each video PES packet.
        /// </param>
        public static byte[] Mux(byte[] videoEs, byte[] adxBytes, double framerate)
        {
            if (videoEs == null || videoEs.Length == 0)
                throw new ArgumentException("Video ES is empty.", nameof(videoEs));
            // adxBytes may be null for video-only SFDs.
            bool hasAudioTrack = adxBytes != null && adxBytes.Length >= 20
                              && adxBytes[0] == 0x80 && adxBytes[1] == 0x00;
            if (adxBytes != null && !hasAudioTrack)
                throw new ArgumentException("ADX data is too small or has bad magic.", nameof(adxBytes));
            if (framerate <= 0)
                throw new ArgumentOutOfRangeException(nameof(framerate),
                    "Framerate must be positive.");

            // ---- Parse ADX header to get audio layout ----
            // Note: adxBytes may be null for video-only SFDs — guarded by hasAudioTrack.

            // Parse ADX header and prepare audio payload only when audio is present.
            byte[] adxPayload = null;
            int frameStride = 0;
            int adxBytesPerVFrame = 0;

            if (hasAudioTrack)
            {
                int copyrightOffset = (adxBytes[2] << 8) | adxBytes[3];
                int audioStart = copyrightOffset + 4;
                int channels = adxBytes[7];
                int sampleRate = (adxBytes[8] << 24) | (adxBytes[9] << 16)
                                    | (adxBytes[10] << 8) | adxBytes[11];

                if (channels < 1 || channels > 2)
                    throw new InvalidDataException(
                        $"Unsupported ADX channel count: {channels}.");
                if (sampleRate <= 0)
                    throw new InvalidDataException("ADX sample rate is non-positive.");
                if (audioStart >= adxBytes.Length)
                    throw new InvalidDataException(
                        "ADX audio start is past end of data.");

                adxPayload = new byte[adxBytes.Length - audioStart];
                Buffer.BlockCopy(adxBytes, audioStart, adxPayload, 0, adxPayload.Length);

                frameStride = AdxBlockSize * channels;
                double samplesPerVFrame = sampleRate / framerate;
                double blocksPerVFrame = samplesPerVFrame / AdxSamplesPerBlock;
                adxBytesPerVFrame = (int)Math.Ceiling(blocksPerVFrame) * frameStride;
            }

            // ---- Build the output ----
            var ms = new MemoryStream();

            int videoPos = 0;
            int audioPos = 0;
            bool wroteFirstPack = false;

            bool audioActive = hasAudioTrack && adxPayload != null && adxPayload.Length > 0;
            while (videoPos < videoEs.Length || (audioActive && audioPos < adxPayload.Length))
            {
                // -- Pack header --
                WritePack(ms);

                // -- Video PES packet --
                if (videoPos < videoEs.Length)
                {
                    int chunk = Math.Min(MaxVideoPesPayload, videoEs.Length - videoPos);
                    WriteVideoPes(ms, videoEs, videoPos, chunk);
                    videoPos += chunk;
                }

                // -- Audio PES packet (omitted for video-only SFDs) --
                if (audioActive && audioPos < adxPayload.Length)
                {
                    int chunk = Math.Min(adxBytesPerVFrame, adxPayload.Length - audioPos);
                    // Round down to a whole number of interleaved ADX frames so we
                    // never split a channel block pair across two PES packets.
                    chunk = (chunk / frameStride) * frameStride;
                    if (chunk <= 0) chunk = Math.Min(frameStride, adxPayload.Length - audioPos);
                    WriteAudioPes(ms, adxPayload, audioPos, chunk);
                    audioPos += chunk;
                }
            }

            // -- MPEG-1 end code --
            ms.Write(new byte[] { 0x00, 0x00, 0x01, 0xB9 }, 0, 4);

            return ms.ToArray();
        }

        // ------------------------------------------------------------------
        // Packet writers
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes an MPEG-1 style pack header (00 00 01 BA + 8 bytes).
        /// We use a fixed SCR of zero — the game's Sofdec reader does not
        /// require accurate SCR values for local file playback.
        /// </summary>
        private static void WritePack(Stream s)
        {
            // Start code
            s.WriteByte(0x00); s.WriteByte(0x00); s.WriteByte(0x01); s.WriteByte(0xBA);

            // MPEG-1 pack: 0x20 | SCR bits. SCR = 0, mux_rate = 0.
            // Byte layout:  0x20 | SCR[32:30]  (top bits of 33-bit SCR, marker bits)
            // For SCR=0, mux_rate=0 the 8 bytes are fixed:
            //   0x21 0x00 0x01 0x00 0x01  (SCR=0 with marker bits)
            //   mux_rate[22:15] mux_rate[14:7] mux_rate[6:0]|1
            s.WriteByte(0x21); s.WriteByte(0x00); s.WriteByte(0x01);
            s.WriteByte(0x00); s.WriteByte(0x01);
            // Mux rate (22 bits, units of 50 bytes/sec). Use a plausible value
            // for MPEG-1 video + audio: ~150 KB/s = 3000 units.
            // Encoded as 3 bytes, top bit of last byte forced to 1 (marker).
            s.WriteByte(0x00); s.WriteByte(0x5D); s.WriteByte(0xC1);
        }

        /// <summary>
        /// Writes a video PES packet (stream id 0xE0) with a minimal MPEG-1 header.
        /// </summary>
        private static void WriteVideoPes(Stream s, byte[] data, int offset, int length)
        {
            WritePes(s, 0xE0, null, data, offset, length);
        }

        /// <summary>
        /// Writes an audio private-stream PES packet (stream id 0xBD) carrying
        /// ADX data with the Sofdec substream prefix.
        /// </summary>
        private static void WriteAudioPes(Stream s, byte[] data, int offset, int length)
        {
            // The 4-byte Sofdec audio substream prefix that SofdecDemuxer strips:
            //   byte 0: substream id (0x40)
            //   bytes 1-3: alignment/frame-count bytes (set to 0x00 0x00 0x00)
            byte[] prefix = new byte[] { AudioSubstreamId, 0x00, 0x00, 0x00 };
            WritePes(s, 0xBD, prefix, data, offset, length);
        }

        /// <summary>
        /// Writes a complete PES packet with an MPEG-1 minimal header (0x0F flag byte).
        ///
        /// PES structure:
        ///   00 00 01 [streamId]          start code (4 bytes)
        ///   [pesLen hi] [pesLen lo]      total PES length after this field (2 bytes)
        ///   0x0F                         MPEG-1 minimal header flag (1 byte)
        ///   [prefix if any]              substream id + alignment bytes
        ///   [payload]
        /// </summary>
        private static void WritePes(Stream s, byte streamId,
            byte[] prefix, byte[] data, int offset, int length)
        {
            int prefixLen = prefix != null ? prefix.Length : 0;
            // PES length = 1 (flag byte) + prefix + payload.
            // Field counts bytes from AFTER the 2-byte length field itself.
            int pesLen = 1 + prefixLen + length;
            if (pesLen > 0xFFFF)
                throw new InvalidOperationException(
                    $"PES packet too large: {pesLen} bytes. Split the payload first.");

            // Start code
            s.WriteByte(0x00); s.WriteByte(0x00); s.WriteByte(0x01);
            s.WriteByte(streamId);

            // Length
            s.WriteByte((byte)(pesLen >> 8));
            s.WriteByte((byte)(pesLen & 0xFF));

            // MPEG-1 minimal header flag (no PTS/DTS)
            s.WriteByte(0x0F);

            // Prefix (substream id + alignment for audio; nothing for video)
            if (prefix != null) s.Write(prefix, 0, prefix.Length);

            // Payload
            s.Write(data, offset, length);
        }
    }
}