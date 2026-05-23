using System;
using System.IO;
using System.Runtime.InteropServices;

namespace psu_archive_explorer
{
    /// <summary>
    /// Imports an MP4 or MKV file back into a Sofdec .sfd container using
    /// the FFmpeg DLLs directly via ffmpeg_helpers.dll — no ffmpeg.exe needed.
    ///
    /// Pipeline:
    ///   1. Demux the original SFD to get target resolution, framerate, and
    ///      ADX audio parameters.
    ///   2. Open the input MP4/MKV via avformat and decode video frames
    ///      (H.264 → YUV420P) and audio frames (AAC → PCM s16).
    ///   3. Re-encode video to MPEG-1 ES using the original SFD dimensions
    ///      and framerate.
    ///   4. Collect PCM audio and build a WAV in memory.
    ///   5. Encode WAV → ADX via AdxEncoder.
    ///   6. Mux MPEG-1 ES + ADX into SFD via SofdecMuxer.
    /// </summary>
    internal static class SfdImporter
    {
        // =====================================================================
        // P/Invoke — ffmpeg_helpers.dll
        // =====================================================================
        private const string Helpers = "ffmpeg_helpers";

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_open_input(
            string path, out int videoStreamIdx, out int audioStreamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_get_stream(IntPtr ctx, int idx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_open_stream_decoder(IntPtr ctx, int streamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_input_width(IntPtr ctx, int streamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_input_height(IntPtr ctx, int streamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_input_framerate(
            IntPtr ctx, int streamIdx, out int num, out int den);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_input_sample_rate(IntPtr ctx, int streamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_input_channels(IntPtr ctx, int streamIdx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_read_packet(IntPtr ctx, IntPtr pkt);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_decode_video_frame(
            IntPtr decCtx, IntPtr pkt, IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_decode_audio_frame(
            IntPtr decCtx, IntPtr pkt, IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_flush_decoder(IntPtr ctx, IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_decode_frame(
            IntPtr decCtx, IntPtr pkt, IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_flush_decode_frame(
            IntPtr decCtx, IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_mpeg1_encoder(
            int width, int height, int fpsNum, int fpsDen, int quality);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_encode_mpeg1_frame(
            IntPtr encCtx, IntPtr frame, IntPtr pkt,
            byte[] outBuf, int outBufSize);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_audio_s16(
            IntPtr frame, short[] outBuf, int outBufSamples, int channels);

        // Reuse existing helpers from SfdExporter
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_video_frame(
            int pixFmt, int width, int height);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_empty_frame();

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_audio_frame(
            int sampleFmt, int channels, int sampleRate, int nbSamples);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_frame_pts(IntPtr frame, long pts);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_get_frame_data(IntPtr frame, int plane);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_linesize(IntPtr frame, int plane);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_width(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_height(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_nb_samples(IntPtr frame);

        // =====================================================================
        // P/Invoke — avcodec-62.dll
        // =====================================================================
        private const string AvCodec = "avcodec-62";

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void avcodec_free_context(ref IntPtr avctx);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr av_packet_alloc();

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_packet_free(ref IntPtr pkt);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_packet_unref(IntPtr pkt);

        // =====================================================================
        // P/Invoke — avformat-62.dll
        // =====================================================================
        private const string AvFormat = "avformat-62";

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern void avformat_close_input(ref IntPtr ps);

        // =====================================================================
        // P/Invoke — avutil-60.dll
        // =====================================================================
        private const string AvUtil = "avutil-60";

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_frame_free(ref IntPtr frame);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_frame_unref(IntPtr frame);

        // =====================================================================
        // Constants
        // =====================================================================
        private const int AV_PIX_FMT_YUV420P = 0;
        private const int AV_SAMPLE_FMT_FLTP = 8;

        // MPEG-1 encode buffer — large enough for one frame at high quality
        private const int EncodeBufSize = 1024 * 1024; // 1 MB per frame max

        // =====================================================================
        // Public API
        // =====================================================================

        public static void ImportToSfd(string inputVideoPath,
                                       byte[] originalSfdBytes,
                                       string outputSfdPath)
        {
            if (!File.Exists(inputVideoPath))
                throw new FileNotFoundException("Input video not found.", inputVideoPath);
            if (originalSfdBytes == null || originalSfdBytes.Length == 0)
                throw new ArgumentException("Original SFD bytes are empty.",
                    nameof(originalSfdBytes));

            // Detect container type to branch decode strategy.
            bool isMkv = inputVideoPath.EndsWith(".mkv",
                StringComparison.OrdinalIgnoreCase);

            // ---- 1. Demux original SFD for target parameters ----
            var demux = new SofdecDemuxer();
            demux.Parse(originalSfdBytes);

            byte[] origVideoEs = demux.GetVideoPayload();
            byte[] origAdxPayload = demux.GetAdxPayload();

            if (origVideoEs.Length < 12)
                throw new InvalidDataException(
                    "Could not find a video stream in the original SFD.");

            int origWidth, origHeight;
            double origFramerate;
            using (var dec = new Mpeg1Decoder(origVideoEs))
            {
                origWidth = dec.Width;
                origHeight = dec.Height;
                origFramerate = dec.Framerate > 0 ? dec.Framerate : 25.0;
            }

            // Convert framerate to exact rational
            int fpsNum, fpsDen;
            GetFpsRational(origFramerate, out fpsNum, out fpsDen);

            bool hasAudio = origAdxPayload.Length > 4
                         && origAdxPayload[0] == 0x80
                         && origAdxPayload[1] == 0x00;

            byte[] adxTemplate = null;
            if (hasAudio)
                adxTemplate = BuildAdxHeader(
                    origAdxPayload, demux.Channels, demux.SampleRate);

            // ---- 2. Open input MP4/MKV ----
            int videoStreamIdx, audioStreamIdx;
            IntPtr inFmtCtx;
            try
            {
                inFmtCtx = ffh_open_input(
                    inputVideoPath, out videoStreamIdx, out audioStreamIdx);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ffh_open_input failed: {ex.GetType().Name}: {ex.Message}");
            }

            if (inFmtCtx == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Could not open input file: {inputVideoPath}\n"
                    + $"videoStream={videoStreamIdx} audioStream={audioStreamIdx}");

            try
            {
                if (videoStreamIdx < 0)
                    throw new InvalidDataException(
                        $"No video stream found in input file. "
                        + $"audioStream={audioStreamIdx}");

                // Get input audio params — fall back to original SFD values
                // if the input stream reports 0 (can happen with some MKVs).
                int inSampleRate = audioStreamIdx >= 0
                    ? ffh_get_input_sample_rate(inFmtCtx, audioStreamIdx) : 0;
                int inChannels = audioStreamIdx >= 0
                    ? ffh_get_input_channels(inFmtCtx, audioStreamIdx) : 0;
                if (inSampleRate <= 0) inSampleRate = demux.SampleRate;
                if (inChannels <= 0) inChannels = demux.Channels;

                // ---- 3. Open decoders ----
                IntPtr videoDecCtx = ffh_open_stream_decoder(inFmtCtx, videoStreamIdx);
                if (videoDecCtx == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "Could not open video decoder.");

                // If no audio stream found in input (e.g. MKV with AAC
                // that our minimal build can't demux), fall back to using
                // the original SFD's ADX audio directly.
                bool useOriginalAudio = hasAudio && audioStreamIdx < 0;
                IntPtr audioDecCtx = IntPtr.Zero;
                if (hasAudio && audioStreamIdx >= 0)
                {
                    audioDecCtx = ffh_open_stream_decoder(inFmtCtx, audioStreamIdx);
                    if (audioDecCtx == IntPtr.Zero)
                    {
                        // Fall back to original audio rather than failing
                        useOriginalAudio = true;
                    }
                }

                try
                {
                    // ---- 4. Open MPEG-1 encoder ----
                    // quality 3 = near-lossless (1=best, 31=worst)
                    IntPtr mpeg1Enc = ffh_alloc_mpeg1_encoder(
                        origWidth, origHeight, fpsNum, fpsDen, 3);
                    if (mpeg1Enc == IntPtr.Zero)
                        throw new InvalidOperationException(
                            "Could not open MPEG-1 encoder.");

                    try
                    {
                        // ---- 5. Decode + encode loop ----
                        var videoEsStream = new MemoryStream();
                        var pcmStream = new MemoryStream();

                        IntPtr pkt = av_packet_alloc();
                        IntPtr videoFrame = ffh_alloc_video_frame(
                            AV_PIX_FMT_YUV420P, origWidth, origHeight);
                        IntPtr audioFrame = IntPtr.Zero;

                        byte[] encodeBuf = new byte[EncodeBufSize];
                        short[] audioPcmBuf = new short[48000 * 2]; // 1s max per frame
                        long videoPts = 0;

                        try
                        {
                            // Main decode loop
                            while (true)
                            {
                                int streamIdx = ffh_read_packet(inFmtCtx, pkt);
                                if (streamIdx < 0) break;

                                if (streamIdx == videoStreamIdx)
                                {
                                    // Allocate an empty frame — the decoder
                                    // fills in format, dimensions, and buffer.
                                    IntPtr decodedFrame = ffh_alloc_empty_frame();
                                    if (decodedFrame == IntPtr.Zero)
                                    {
                                        av_packet_unref(pkt);
                                        continue;
                                    }
                                    try
                                    {
                                        if ((isMkv
                                            ? ffh_decode_video_frame(videoDecCtx, pkt, decodedFrame)
                                            : ffh_decode_frame(videoDecCtx, pkt, decodedFrame)) > 0)
                                        {
                                            int srcW = ffh_get_frame_width(decodedFrame);
                                            int srcH = ffh_get_frame_height(decodedFrame);
                                            if (srcW <= 0) srcW = origWidth;
                                            if (srcH <= 0) srcH = origHeight;

                                            CopyOrScaleFrame(decodedFrame, videoFrame,
                                                Math.Min(srcW, origWidth),
                                                Math.Min(srcH, origHeight));
                                            ffh_set_frame_pts(videoFrame, videoPts++);

                                            int bytes = ffh_encode_mpeg1_frame(
                                                mpeg1Enc, videoFrame, pkt,
                                                encodeBuf, EncodeBufSize);
                                            if (bytes > 0)
                                                videoEsStream.Write(encodeBuf, 0, bytes);
                                        }
                                    }
                                    finally
                                    {
                                        av_frame_free(ref decodedFrame);
                                    }
                                }
                                else if (streamIdx == audioStreamIdx
                                         && audioDecCtx != IntPtr.Zero)
                                {
                                    // Decode AAC → PCM
                                    IntPtr aFrame = ffh_alloc_empty_frame();
                                    if (aFrame == IntPtr.Zero)
                                    {
                                        av_packet_unref(pkt);
                                        continue;
                                    }
                                    try
                                    {
                                        if ((isMkv
                                            ? ffh_decode_audio_frame(audioDecCtx, pkt, aFrame)
                                            : ffh_decode_frame(audioDecCtx, pkt, aFrame)) > 0)
                                        {
                                            int samples = ffh_get_frame_nb_samples(aFrame);
                                            if (audioPcmBuf.Length < samples * inChannels)
                                                audioPcmBuf = new short[samples * inChannels * 2];

                                            int got = ffh_get_audio_s16(
                                                aFrame, audioPcmBuf,
                                                samples * inChannels, inChannels);
                                            WritePcmToStream(pcmStream,
                                                audioPcmBuf, got * inChannels);
                                        }
                                    }
                                    finally
                                    {
                                        av_frame_free(ref aFrame);
                                    }
                                }

                                av_packet_unref(pkt);
                            }

                            // Flush video decoder
                            IntPtr flushFrame = ffh_alloc_empty_frame();
                            try
                            {
                                while ((isMkv
                                    ? ffh_flush_decoder(videoDecCtx, flushFrame)
                                    : ffh_flush_decode_frame(videoDecCtx, flushFrame)) > 0)
                                {
                                    CopyOrScaleFrame(flushFrame, videoFrame,
                                        origWidth, origHeight);
                                    ffh_set_frame_pts(videoFrame, videoPts++);
                                    int bytes = ffh_encode_mpeg1_frame(
                                        mpeg1Enc, videoFrame, pkt,
                                        encodeBuf, EncodeBufSize);
                                    if (bytes > 0)
                                        videoEsStream.Write(encodeBuf, 0, bytes);
                                    av_frame_unref(flushFrame);
                                }
                            }
                            finally
                            {
                                av_frame_free(ref flushFrame);
                            }

                            // Flush MPEG-1 encoder
                            while (true)
                            {
                                int bytes = ffh_encode_mpeg1_frame(
                                    mpeg1Enc, IntPtr.Zero, pkt,
                                    encodeBuf, EncodeBufSize);
                                if (bytes <= 0) break;
                                videoEsStream.Write(encodeBuf, 0, bytes);
                            }
                        }
                        finally
                        {
                            av_packet_free(ref pkt);
                            av_frame_free(ref videoFrame);
                        }

                        // ---- 6. Build audio ----
                        byte[] mpeg1Es = videoEsStream.ToArray();
                        if (mpeg1Es.Length == 0)
                            throw new InvalidDataException(
                                "MPEG-1 encoder produced no output. " +
                                "Check that the input contains a valid video track.");

                        byte[] adxBytes = null;
                        if (useOriginalAudio)
                        {
                            // No audio stream found in input — reuse original
                            // SFD audio directly (already demuxed as ADX payload).
                            adxBytes = BuildAdxHeader(
                                origAdxPayload, demux.Channels, demux.SampleRate);
                        }
                        else if (hasAudio && pcmStream.Length > 0
                                 && adxTemplate != null)
                        {
                            byte[] wavBytes = BuildWav(
                                pcmStream.ToArray(), inChannels, inSampleRate);
                            adxBytes = AdxEncoder.EncodeFromWav(wavBytes, adxTemplate);
                        }

                        // ---- 7. Mux into SFD ----
                        byte[] sfdBytes = SofdecMuxer.Mux(
                            mpeg1Es, adxBytes, origFramerate);
                        File.WriteAllBytes(outputSfdPath, sfdBytes);
                    }
                    finally
                    {
                        avcodec_free_context(ref mpeg1Enc);
                    }
                }
                finally
                {
                    avcodec_free_context(ref videoDecCtx);
                    if (audioDecCtx != IntPtr.Zero)
                        avcodec_free_context(ref audioDecCtx);
                }
            }
            finally
            {
                avformat_close_input(ref inFmtCtx);
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        /// <summary>
        /// Copy decoded frame into the encoder frame, handling stride differences.
        /// Both frames must be YUV420P. If sizes differ the copy is clipped to
        /// the smaller of the two — proper scaling would require swscale.
        /// </summary>
        private static void CopyOrScaleFrame(IntPtr src, IntPtr dst,
            int dstWidth, int dstHeight)
        {
            CopyPlane(src, dst, 0, dstWidth, dstHeight);
            CopyPlane(src, dst, 1, dstWidth / 2, dstHeight / 2);
            CopyPlane(src, dst, 2, dstWidth / 2, dstHeight / 2);
        }

        private static void CopyPlane(IntPtr src, IntPtr dst,
            int plane, int width, int height)
        {
            IntPtr srcData = ffh_get_frame_data(src, plane);
            IntPtr dstData = ffh_get_frame_data(dst, plane);
            int srcStride = ffh_get_frame_linesize(src, plane);
            int dstStride = ffh_get_frame_linesize(dst, plane);

            if (srcData == IntPtr.Zero || dstData == IntPtr.Zero) return;

            byte[] row = new byte[width];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(srcData + y * srcStride, row, 0, width);
                Marshal.Copy(row, 0, dstData + y * dstStride, width);
            }
        }

        private static void WritePcmToStream(MemoryStream ms,
            short[] buf, int count)
        {
            byte[] bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                bytes[i * 2] = (byte)(buf[i] & 0xFF);
                bytes[i * 2 + 1] = (byte)((buf[i] >> 8) & 0xFF);
            }
            ms.Write(bytes, 0, bytes.Length);
        }

        private static byte[] BuildWav(byte[] pcm, int channels, int sampleRate)
        {
            int dataBytes = pcm.Length;
            int riffSize = 36 + dataBytes;
            byte[] wav = new byte[8 + riffSize];
            int p = 0;

            wav[p++] = (byte)'R'; wav[p++] = (byte)'I';
            wav[p++] = (byte)'F'; wav[p++] = (byte)'F';
            wav[p++] = (byte)riffSize; wav[p++] = (byte)(riffSize >> 8);
            wav[p++] = (byte)(riffSize >> 16); wav[p++] = (byte)(riffSize >> 24);
            wav[p++] = (byte)'W'; wav[p++] = (byte)'A';
            wav[p++] = (byte)'V'; wav[p++] = (byte)'E';

            wav[p++] = (byte)'f'; wav[p++] = (byte)'m';
            wav[p++] = (byte)'t'; wav[p++] = (byte)' ';
            wav[p++] = 16; wav[p++] = 0; wav[p++] = 0; wav[p++] = 0;
            wav[p++] = 1; wav[p++] = 0;  // PCM
            wav[p++] = (byte)channels; wav[p++] = 0;
            wav[p++] = (byte)sampleRate; wav[p++] = (byte)(sampleRate >> 8);
            wav[p++] = (byte)(sampleRate >> 16); wav[p++] = (byte)(sampleRate >> 24);
            int byteRate = sampleRate * channels * 2;
            wav[p++] = (byte)byteRate; wav[p++] = (byte)(byteRate >> 8);
            wav[p++] = (byte)(byteRate >> 16); wav[p++] = (byte)(byteRate >> 24);
            wav[p++] = (byte)(channels * 2); wav[p++] = 0;
            wav[p++] = 16; wav[p++] = 0;

            wav[p++] = (byte)'d'; wav[p++] = (byte)'a';
            wav[p++] = (byte)'t'; wav[p++] = (byte)'a';
            wav[p++] = (byte)dataBytes; wav[p++] = (byte)(dataBytes >> 8);
            wav[p++] = (byte)(dataBytes >> 16); wav[p++] = (byte)(dataBytes >> 24);
            Buffer.BlockCopy(pcm, 0, wav, p, pcm.Length);
            return wav;
        }

        private static void GetFpsRational(double fps,
            out int fpsNum, out int fpsDen)
        {
            if (Math.Abs(fps - 30000.0 / 1001.0) < 0.01) { fpsNum = 30000; fpsDen = 1001; }
            else if (Math.Abs(fps - 24000.0 / 1001.0) < 0.01) { fpsNum = 24000; fpsDen = 1001; }
            else if (Math.Abs(fps - 60000.0 / 1001.0) < 0.01) { fpsNum = 60000; fpsDen = 1001; }
            else if (Math.Abs(fps - 25.0) < 0.01) { fpsNum = 25; fpsDen = 1; }
            else if (Math.Abs(fps - 30.0) < 0.01) { fpsNum = 30; fpsDen = 1; }
            else if (Math.Abs(fps - 24.0) < 0.01) { fpsNum = 24; fpsDen = 1; }
            else if (Math.Abs(fps - 50.0) < 0.01) { fpsNum = 50; fpsDen = 1; }
            else if (Math.Abs(fps - 60.0) < 0.01) { fpsNum = 60; fpsDen = 1; }
            else { fpsNum = (int)Math.Round(fps); fpsDen = 1; }
        }

        private static byte[] BuildAdxHeader(byte[] payload,
            int channels, int sampleRate)
        {
            const int BlockSize = 18;
            const int SamplesPerBlock = 32;
            const int CopyrightOffset = 0x001C;
            const int AudioStart = CopyrightOffset + 4;

            int frameStride = BlockSize * channels;
            int completeFrames = payload.Length / frameStride;
            int totalSamples = completeFrames * SamplesPerBlock;
            int usableBytes = completeFrames * frameStride;

            byte[] adx = new byte[AudioStart + usableBytes];
            adx[0] = 0x80; adx[1] = 0x00;
            adx[2] = (byte)(CopyrightOffset >> 8);
            adx[3] = (byte)(CopyrightOffset & 0xFF);
            adx[4] = 0x03; adx[5] = BlockSize; adx[6] = 4;
            adx[7] = (byte)channels;
            adx[8] = (byte)(sampleRate >> 24); adx[9] = (byte)(sampleRate >> 16);
            adx[10] = (byte)(sampleRate >> 8); adx[11] = (byte)(sampleRate & 0xFF);
            adx[12] = (byte)(totalSamples >> 24); adx[13] = (byte)(totalSamples >> 16);
            adx[14] = (byte)(totalSamples >> 8); adx[15] = (byte)(totalSamples & 0xFF);
            adx[16] = 0x01; adx[17] = 0xF4;
            adx[18] = 0x03; adx[19] = 0x00;

            byte[] cri = System.Text.Encoding.ASCII.GetBytes("(c)CRI");
            Array.Copy(cri, 0, adx, CopyrightOffset + 2, 6);
            Buffer.BlockCopy(payload, 0, adx, AudioStart, usableBytes);
            return adx;
        }
    }
}