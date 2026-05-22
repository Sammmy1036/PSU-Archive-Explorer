using System;
using System.IO;
using System.Runtime.InteropServices;

namespace psu_archive_explorer
{
    /// <summary>
    /// Exports a Sofdec .sfd file to MP4 or MKV using the FFmpeg libraries
    /// via a thin C helper DLL (ffmpeg_helpers.dll) that wraps struct field
    /// access so we never need hardcoded byte offsets in C#.
    ///
    /// Pipeline:
    ///   1. Demux the SFD to get the raw MPEG-1 video ES and ADX audio.
    ///   2. Decode ADX to PCM WAV using AdxDecoder (pure C#).
    ///   3. Open the MPEG-1 ES via avformat, decode frames, re-encode to H.264.
    ///   4. Encode PCM audio to AAC.
    ///   5. Write MP4 or MKV container via avformat.
    /// </summary>
    internal static class SfdExporter
    {
        // =====================================================================
        // P/Invoke — ffmpeg_helpers.dll (our thin C wrapper)
        // =====================================================================
        private const string Helpers = "ffmpeg_helpers";

        // --- Format context ---
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_output_context(string format, string filename);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_new_stream(IntPtr ctx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_oformat_flags(IntPtr ctx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_stream_timebase(IntPtr st, int num, int den);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_stream_timebase(IntPtr st, out int num, out int den);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_stream_index(IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_get_stream_codecpar(IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_avg_frame_rate(IntPtr st, out int num, out int den);

        // --- Codec context ---
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_encoder_context_by_id(int id);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_encoder_context_by_name(string name);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_decoder_context_by_id(int id);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_find_encoder_by_name(string name);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_find_decoder_by_id(int id);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_open_encoder(IntPtr ctx, IntPtr codec);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_open_decoder(IntPtr ctx, IntPtr codec);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_video_params(IntPtr ctx,
            int width, int height, int pixFmt,
            int tbNum, int tbDen, int frNum, int frDen, int flags);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_video_params(IntPtr ctx,
            out int width, out int height, out int pixFmt);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_audio_params(IntPtr ctx,
            int sampleRate, int channels, int sampleFmt, int bitRate, int flags);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_audio_params(IntPtr ctx,
            out int sampleRate, out int channels, out int sampleFmt, out int frameSize);

        // --- Frame ---
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_video_frame(int pixFmt, int width, int height);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_alloc_audio_frame(
            int sampleFmt, int channels, int sampleRate, int nbSamples);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_frame_pts(IntPtr frame, long pts);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_width(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_height(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_format(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_nb_samples(IntPtr frame);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr ffh_get_frame_data(IntPtr frame, int plane);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_frame_linesize(IntPtr frame, int plane);

        // --- Packet ---
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_set_packet_stream_index(IntPtr pkt, int idx);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_packet_stream_index(IntPtr pkt);

        // --- Stream info ---
        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_stream_codec_id(IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_codecpar_width(IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_get_codecpar_height(IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_copy_codecpar_to_ctx(IntPtr ctx, IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_encoder_available_by_id(int id);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_codec_timebase(
            IntPtr ctx, out int num, out int den);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern long ffh_get_pkt_pts(IntPtr pkt);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern long ffh_get_pkt_dts(IntPtr pkt);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_get_ctx_timebase(
            IntPtr ctx, out int num, out int den);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_write_video_packet(
            IntPtr fmtCtx, IntPtr pkt, IntPtr encCtx, IntPtr st);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_debug_to_file(
            IntPtr encCtx, IntPtr st, IntPtr pkt);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_debug_packet(
            IntPtr pkt, IntPtr encCtx, IntPtr st,
            out long ptsBefore, out long ptsAfter,
            out int encTbNum, out int encTbDen,
            out int stTbNum, out int stTbDen);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_encoder_available_by_name(string name);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern void ffh_packet_rescale_ts(
            IntPtr pkt, int srcNum, int srcDen, int dstNum, int dstDen);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_avio_open(IntPtr ctx, string url);

        [DllImport(Helpers, CallingConvention = CallingConvention.Cdecl)]
        private static extern int ffh_avio_close(IntPtr ctx);

        // =====================================================================
        // P/Invoke — FFmpeg DLLs directly (only for functions not in helpers)
        // =====================================================================
        private const string AvUtil = "avutil-60";
        private const string AvCodec = "avcodec-62";
        private const string AvFormat = "avformat-62";

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_frame_free(ref IntPtr frame);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_frame_unref(IntPtr frame);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_frame_make_writable(IntPtr frame);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_opt_set(IntPtr obj, string name, string val, int flags);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_dict_set(ref IntPtr dict, string key, string value, int flags);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_dict_free(ref IntPtr dict);

        [DllImport(AvUtil, CallingConvention = CallingConvention.Cdecl)]
        private static extern long av_rescale_q(long a, int bqNum, int bqDen, int cqNum, int cqDen);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void avcodec_free_context(ref IntPtr avctx);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avcodec_parameters_from_context(IntPtr par, IntPtr codec);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avcodec_send_frame(IntPtr avctx, IntPtr frame);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avcodec_receive_packet(IntPtr avctx, IntPtr pkt);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avcodec_send_packet(IntPtr avctx, IntPtr pkt);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avcodec_receive_frame(IntPtr avctx, IntPtr frame);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr av_packet_alloc();

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_packet_free(ref IntPtr pkt);

        [DllImport(AvCodec, CallingConvention = CallingConvention.Cdecl)]
        private static extern void av_packet_unref(IntPtr pkt);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern void avformat_free_context(IntPtr ctx);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avformat_write_header(IntPtr ctx, ref IntPtr options);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_interleaved_write_frame(IntPtr ctx, IntPtr pkt);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_write_trailer(IntPtr ctx);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avformat_open_input(
            ref IntPtr ps, string url, IntPtr fmt, ref IntPtr options);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int avformat_find_stream_info(IntPtr ic, IntPtr options);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern void avformat_close_input(ref IntPtr ps);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern int av_read_frame(IntPtr ctx, IntPtr pkt);

        [DllImport(AvFormat, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr avformat_new_stream(IntPtr ctx, IntPtr codec);

        // =====================================================================
        // Constants
        // =====================================================================
        private const int AV_CODEC_ID_MPEG1VIDEO = 1;
        private const int AV_CODEC_ID_AAC = 0x10060; // 65632 decimal (0x10000 + 96)

        private const int AV_PIX_FMT_YUV420P = 0;
        private const int AV_SAMPLE_FMT_FLTP = 8;

        private const int AVIO_FLAG_WRITE = 2;
        private const int AVFMT_NOFILE = 0x0001;
        private const int AV_CODEC_FLAG_GLOBAL_HEADER = 1 << 22;
        private const int AV_OPT_SEARCH_CHILDREN = 1;

        // =====================================================================
        // Public API
        // =====================================================================

        public static void ExportToVideo(byte[] sfdBytes, string outputPath)
        {
            string ext = Path.GetExtension(outputPath).ToLowerInvariant();
            if (ext != ".mp4" && ext != ".mkv")
                throw new ArgumentException(
                    $"Unsupported output format: {ext}. Use .mp4 or .mkv.");

            // ---- 1. Demux SFD ----
            var demux = new SofdecDemuxer();
            demux.Parse(sfdBytes);
            byte[] videoEs = demux.GetVideoPayload();
            byte[] adxPayload = demux.GetAdxPayload();

            if (videoEs.Length == 0)
                throw new InvalidDataException("No video stream in SFD.");

            bool hasAudio = adxPayload.Length > 4
                         && adxPayload[0] == 0x80
                         && adxPayload[1] == 0x00;

            // ---- 2. Decode ADX → WAV (pure C#) ----
            byte[] wavBytes = null;
            if (hasAudio)
                wavBytes = AdxDecoder.DecodeToWav(adxPayload);

            // ---- 3. Read true framerate from pl_mpeg ----
            // ffh_get_avg_frame_rate on the demuxed ES returns wrong values.
            // Mpeg1Decoder reads directly from the MPEG-1 sequence header.
            double plmFramerate = 25.0;
            using (var plmDec = new Mpeg1Decoder(videoEs))
            {
                plmFramerate = plmDec.Framerate > 0 ? plmDec.Framerate : 25.0;
            }

            // ---- 4. Write MPEG-1 ES to temp file ----
            string tmpVideo = Path.Combine(
                Path.GetTempPath(),
                Path.GetRandomFileName().Replace(".", "") + ".mpg");

            try
            {
                File.WriteAllBytes(tmpVideo, videoEs);
                ExportCore(tmpVideo, wavBytes, outputPath, ext,
                    hasAudio, plmFramerate);
            }
            finally
            {
                TryDelete(tmpVideo);
            }
        }

        // =====================================================================
        // Core pipeline
        // =====================================================================

        private static void ExportCore(
            string videoTmpPath, byte[] wavBytes,
            string outputPath, string ext, bool hasAudio,
            double plmFramerate = 25.0)
        {
            bool isMp4 = ext == ".mp4";

            // ---- Open input MPEG-1 ES ----
            IntPtr inFmtCtx = IntPtr.Zero;
            IntPtr nullOpts = IntPtr.Zero;
            int ret = avformat_open_input(
                ref inFmtCtx, videoTmpPath, IntPtr.Zero, ref nullOpts);
            if (ret < 0)
                throw new InvalidOperationException(
                    $"avformat_open_input failed: {ret}");

            try
            {
                ret = avformat_find_stream_info(inFmtCtx, IntPtr.Zero);
                if (ret < 0)
                    throw new InvalidOperationException(
                        $"avformat_find_stream_info failed: {ret}");

                // Get first video stream — AVFormatContext.streams[0]
                // nb_streams at offset 44, streams** at offset 48 on x64
                int nbStreams = Marshal.ReadInt32(inFmtCtx, 44);
                IntPtr streamsPtr = Marshal.ReadIntPtr(inFmtCtx, 48);
                if (nbStreams < 1 || streamsPtr == IntPtr.Zero)
                    throw new InvalidOperationException("No streams in input.");
                IntPtr inVideoStream = Marshal.ReadIntPtr(streamsPtr, 0);

                int inputWidth = ffh_get_codecpar_width(inVideoStream);
                int inputHeight = ffh_get_codecpar_height(inVideoStream);
                int codecId = ffh_get_stream_codec_id(inVideoStream);

                ffh_get_avg_frame_rate(inVideoStream, out int frNum, out int frDen);
                if (frNum <= 0 || frDen <= 0) { frNum = 25; frDen = 1; }

                // Use pl_mpeg framerate (reads MPEG-1 sequence header correctly).
                // fpsNum/fpsDen = framerate as rational (e.g. 30/1 for 29.97 rounded)
                // tbNum/tbDen  = time_base = 1/fps (reciprocal of framerate)
                double fps = plmFramerate > 0
                    ? plmFramerate : (double)frNum / frDen;
                int fpsNum, fpsDen;
                if (Math.Abs(fps - 30000.0 / 1001.0) < 0.01) { fpsNum = 30000; fpsDen = 1001; }
                else if (Math.Abs(fps - 24000.0 / 1001.0) < 0.01) { fpsNum = 24000; fpsDen = 1001; }
                else if (Math.Abs(fps - 60000.0 / 1001.0) < 0.01) { fpsNum = 60000; fpsDen = 1001; }
                else if (Math.Abs(fps - 25.0) < 0.01) { fpsNum = 25; fpsDen = 1; }
                else if (Math.Abs(fps - 30.0) < 0.01) { fpsNum = 30; fpsDen = 1; }
                else if (Math.Abs(fps - 24.0) < 0.01) { fpsNum = 24; fpsDen = 1; }
                else if (Math.Abs(fps - 50.0) < 0.01) { fpsNum = 50; fpsDen = 1; }
                else if (Math.Abs(fps - 60.0) < 0.01) { fpsNum = 60; fpsDen = 1; }
                else { fpsNum = (int)Math.Round(fps); fpsDen = 1; }
                int tbNum = fpsDen;
                int tbDen = fpsNum;

                // ---- Set up output format context ----
                string fmtName = isMp4 ? "mp4" : "matroska";
                IntPtr outFmtCtx = ffh_alloc_output_context(fmtName, outputPath);
                if (outFmtCtx == IntPtr.Zero)
                    throw new InvalidOperationException(
                        "ffh_alloc_output_context failed.");

                try
                {
                    // ---- Video encoder (libx264) ----
                    IntPtr videoCodec = ffh_find_encoder_by_name("libx264");
                    if (videoCodec == IntPtr.Zero)
                        throw new InvalidOperationException(
                            "libx264 encoder not found.");

                    IntPtr videoEncCtx = ffh_alloc_encoder_context_by_name("libx264");
                    if (videoEncCtx == IntPtr.Zero)
                        throw new InvalidOperationException(
                            "Could not allocate video encoder context.");

                    try
                    {
                        // Both MP4 and MKV require global header (SPS/PPS)
                        // to be embedded in extradata before writing the
                        // container header.
                        int videoFlags = AV_CODEC_FLAG_GLOBAL_HEADER;
                        // Use 1/framerate as the encoder timebase.
                        // libx264 expects time_base = 1/fps so PTS
                        // increments by 1 per frame.
                        ffh_set_video_params(videoEncCtx,
                            inputWidth, inputHeight, AV_PIX_FMT_YUV420P,
                            tbNum, tbDen,    // time_base = 1/fps
                            fpsNum, fpsDen,  // framerate = fps/1
                            videoFlags);

                        av_opt_set(videoEncCtx, "preset", "fast",
                            AV_OPT_SEARCH_CHILDREN);
                        av_opt_set(videoEncCtx, "crf", "18",
                            AV_OPT_SEARCH_CHILDREN);

                        ret = ffh_open_encoder(videoEncCtx, videoCodec);
                        if (ret < 0)
                            throw new InvalidOperationException(
                                $"ffh_open_encoder (video) failed: {ret}");

                        IntPtr videoStream = avformat_new_stream(
                            outFmtCtx, IntPtr.Zero);
                        if (videoStream == IntPtr.Zero)
                            throw new InvalidOperationException(
                                "avformat_new_stream (video) failed.");

                        ret = avcodec_parameters_from_context(
                            ffh_get_stream_codecpar(videoStream), videoEncCtx);
                        if (ret < 0)
                            throw new InvalidOperationException(
                                $"avcodec_parameters_from_context (video) failed: {ret}");

                        // MP4: 1/fps timebase (identity rescale).
                        // MKV: 1/1000 ms timebase, C-side rescale.
                        if (isMp4)
                            ffh_set_stream_timebase(videoStream, tbNum, tbDen);
                        else
                            ffh_set_stream_timebase(videoStream, 1, 1000);

                        // ---- Audio encoder (AAC) ----
                        IntPtr audioEncCtx = IntPtr.Zero;
                        IntPtr audioStream = IntPtr.Zero;

                        if (hasAudio && wavBytes != null)
                        {
                            ParseWavHeader(wavBytes,
                                out int wavChannels, out int wavSampleRate);

                            // Use by-name lookup since the codec ID enum
                            // value may differ between FFmpeg builds.
                            IntPtr audioCodec = ffh_find_encoder_by_name("aac");
                            if (audioCodec == IntPtr.Zero)
                                throw new InvalidOperationException(
                                    "AAC encoder not found in avcodec DLL.");

                            audioEncCtx = ffh_alloc_encoder_context_by_name("aac");
                            if (audioEncCtx == IntPtr.Zero)
                                throw new InvalidOperationException(
                                    "Could not allocate AAC encoder context.");

                            int audioFlags = AV_CODEC_FLAG_GLOBAL_HEADER;
                            ffh_set_audio_params(audioEncCtx,
                                wavSampleRate, wavChannels,
                                AV_SAMPLE_FMT_FLTP, 192000, audioFlags);

                            ret = ffh_open_encoder(audioEncCtx, audioCodec);
                            if (ret < 0)
                            {
                                avcodec_free_context(ref audioEncCtx);
                                throw new InvalidOperationException(
                                    $"ffh_open_encoder (audio) failed: {ret}");
                            }

                            audioStream = avformat_new_stream(
                                outFmtCtx, IntPtr.Zero);
                            if (audioStream == IntPtr.Zero)
                            {
                                avcodec_free_context(ref audioEncCtx);
                                throw new InvalidOperationException(
                                    "avformat_new_stream (audio) failed.");
                            }

                            ret = avcodec_parameters_from_context(
                                ffh_get_stream_codecpar(audioStream),
                                audioEncCtx);
                            if (ret < 0)
                            {
                                avcodec_free_context(ref audioEncCtx);
                                throw new InvalidOperationException(
                                    $"avcodec_parameters_from_context (audio) failed: {ret}");
                            }

                            ffh_get_audio_params(audioEncCtx,
                                out int sr, out int ch, out int fmt, out int fs);
                            // MKV: use 1/1000 to match video stream timebase.
                            // MP4: use 1/sampleRate for AAC.
                            if (isMp4)
                                ffh_set_stream_timebase(audioStream, 1, sr);
                            else
                                ffh_set_stream_timebase(audioStream, 1, 1000);
                        }

                        // ---- Open output file ----
                        int fmtFlags = ffh_oformat_flags(outFmtCtx);
                        if ((fmtFlags & AVFMT_NOFILE) == 0)
                        {
                            ret = ffh_avio_open(outFmtCtx, outputPath);
                            if (ret < 0)
                                throw new InvalidOperationException(
                                    $"ffh_avio_open failed: {ret}");
                        }

                        // Write header
                        IntPtr muxOpts = IntPtr.Zero;
                        if (isMp4)
                            av_dict_set(ref muxOpts, "movflags", "faststart", 0);

                        ret = avformat_write_header(outFmtCtx, ref muxOpts);
                        if (muxOpts != IntPtr.Zero) av_dict_free(ref muxOpts);
                        if (ret < 0)
                            throw new InvalidOperationException(
                                $"avformat_write_header failed: {ret}");

                        // ---- Transcode video ----
                        // MP4: identity rescale 1/fps -> 1/fps.
                        // MKV: use ffh_write_video_packet which rescales
                        //      in C using correct AVRational struct access.
                        TranscodeVideo(inFmtCtx, inVideoStream, codecId,
                            videoEncCtx, videoStream, outFmtCtx,
                            tbNum, tbDen,
                            skipRescale: false,
                            useCRescale: !isMp4);

                        // ---- Encode audio ----
                        if (hasAudio && audioEncCtx != IntPtr.Zero
                            && wavBytes != null)
                        {
                            EncodeAudio(wavBytes, audioEncCtx,
                                audioStream, outFmtCtx, isMp4);
                            avcodec_free_context(ref audioEncCtx);
                        }

                        // ---- Finalize ----
                        av_write_trailer(outFmtCtx);

                        if ((fmtFlags & AVFMT_NOFILE) == 0)
                        {
                            ffh_avio_close(outFmtCtx);
                        }
                    }
                    finally
                    {
                        avcodec_free_context(ref videoEncCtx);
                    }
                }
                finally
                {
                    avformat_free_context(outFmtCtx);
                }
            }
            finally
            {
                avformat_close_input(ref inFmtCtx);
            }
        }

        // =====================================================================
        // Video transcode
        // =====================================================================

        private static void TranscodeVideo(
            IntPtr inFmtCtx, IntPtr inVideoStream, int codecId,
            IntPtr encCtx, IntPtr outStream,
            IntPtr outFmtCtx, int srcTbNum, int srcTbDen,
            bool skipRescale = false,
            bool useCRescale = false)
        {
            IntPtr decoder = ffh_find_decoder_by_id(codecId);
            IntPtr decCtx = ffh_alloc_decoder_context_by_id(codecId);
            ffh_copy_codecpar_to_ctx(decCtx, inVideoStream);
            ffh_open_decoder(decCtx, decoder);

            ffh_get_video_params(encCtx, out int w, out int h, out int fmt);

            IntPtr inFrame = ffh_alloc_video_frame(fmt, w, h);
            IntPtr outFrame = ffh_alloc_video_frame(fmt, w, h);
            IntPtr pkt = av_packet_alloc();

            int outStreamIdx = ffh_get_stream_index(outStream);
            ffh_get_stream_timebase(outStream, out int tbNum, out int tbDen);

            long pts = 0;
            int frameCount = 0;

            try
            {
                while (true)
                {
                    int r = av_read_frame(inFmtCtx, pkt);
                    if (r < 0) break;

                    r = avcodec_send_packet(decCtx, pkt);
                    av_packet_unref(pkt);
                    if (r < 0) continue;

                    while (avcodec_receive_frame(decCtx, inFrame) >= 0)
                    {
                        frameCount++;
                        av_frame_make_writable(outFrame);
                        CopyYuvPlanes(inFrame, outFrame, w, h);
                        // Use our own counter for PTS — do not inherit
                        // from inFrame which uses the input stream timebase.
                        ffh_set_frame_pts(outFrame, pts);
                        pts++;

                        avcodec_send_frame(encCtx, outFrame);
                        av_frame_unref(inFrame);

                        DrainVideoEncoder(encCtx, pkt, outFmtCtx,
                            outStreamIdx, srcTbNum, srcTbDen, tbNum, tbDen,
                            skipRescale,
                            useCRescale ? outStream : IntPtr.Zero);
                    }
                }

                // Flush decoder
                avcodec_send_packet(decCtx, IntPtr.Zero);
                while (avcodec_receive_frame(decCtx, inFrame) >= 0)
                {
                    av_frame_make_writable(outFrame);
                    CopyYuvPlanes(inFrame, outFrame, w, h);
                    ffh_set_frame_pts(outFrame, pts);
                    pts++;
                    avcodec_send_frame(encCtx, outFrame);
                    av_frame_unref(inFrame);
                    DrainVideoEncoder(encCtx, pkt, outFmtCtx,
                        outStreamIdx, srcTbNum, srcTbDen, tbNum, tbDen, skipRescale);
                }

                // Flush encoder
                avcodec_send_frame(encCtx, IntPtr.Zero);
                DrainVideoEncoder(encCtx, pkt, outFmtCtx,
                    outStreamIdx, srcTbNum, srcTbDen, tbNum, tbDen, skipRescale);
            }
            finally
            {
                av_frame_free(ref inFrame);
                av_frame_free(ref outFrame);
                av_packet_free(ref pkt);
                avcodec_free_context(ref decCtx);
            }
        }

        private static void DrainVideoEncoder(
            IntPtr encCtx, IntPtr pkt, IntPtr outFmtCtx,
            int streamIdx, int srcNum, int srcDen, int dstNum, int dstDen,
            bool skipRescale = false,
            IntPtr outStream = default)
        {
            while (avcodec_receive_packet(encCtx, pkt) >= 0)
            {
                if (outStream != IntPtr.Zero)
                {
                    // Use C-side rescale which has correct access to
                    // AVRational struct fields — avoids all timebase
                    // guessing issues from the C# side.
                    ffh_write_video_packet(outFmtCtx, pkt, encCtx, outStream);
                }
                else
                {
                    if (!skipRescale)
                        ffh_packet_rescale_ts(pkt, srcNum, srcDen, dstNum, dstDen);
                    ffh_set_packet_stream_index(pkt, streamIdx);
                    av_interleaved_write_frame(outFmtCtx, pkt);
                }
                av_packet_unref(pkt);
            }
        }

        // =====================================================================
        // Audio encode
        // =====================================================================

        private static void EncodeAudio(
            byte[] wavBytes, IntPtr encCtx,
            IntPtr outStream, IntPtr outFmtCtx,
            bool isMp4 = true)
        {
            ParseWavHeader(wavBytes, out int channels, out int sampleRate);
            int dataOffset = FindWavDataOffset(wavBytes);
            int totalSamples = (wavBytes.Length - dataOffset) / (2 * channels);

            ffh_get_audio_params(encCtx,
                out int sr, out int ch, out int fmt, out int frameSize);
            if (frameSize <= 0) frameSize = 1024;

            // Pad WAV data so totalSamples is a multiple of frameSize.
            // Without this the last partial frame is silently dropped by
            // the AAC encoder, cutting audio 25-30 seconds short.
            int remainder = totalSamples % frameSize;
            if (remainder != 0)
            {
                int paddingSamples = frameSize - remainder;
                int paddingBytes = paddingSamples * channels * 2;
                byte[] padded = new byte[wavBytes.Length + paddingBytes];
                Buffer.BlockCopy(wavBytes, 0, padded, 0, wavBytes.Length);
                // Remaining bytes are already zero (silence).
                wavBytes = padded;
                totalSamples += paddingSamples;
            }

            ffh_get_stream_timebase(outStream, out int tbNum, out int tbDen);
            int outStreamIdx = ffh_get_stream_index(outStream);

            IntPtr pkt = av_packet_alloc();
            long pts = 0;
            int samplePos = 0;
            int wavOffset = dataOffset;

            try
            {
                while (samplePos < totalSamples)
                {
                    int samplesThisFrame = Math.Min(frameSize,
                        totalSamples - samplePos);

                    IntPtr frame = ffh_alloc_audio_frame(
                        fmt, channels, sampleRate, frameSize);

                    FillAudioFrame(frame, wavBytes, wavOffset,
                        samplesThisFrame, channels, frameSize);

                    ffh_set_frame_pts(frame, pts);
                    pts += samplesThisFrame;
                    samplePos += samplesThisFrame;
                    wavOffset += samplesThisFrame * channels * 2;

                    avcodec_send_frame(encCtx, frame);
                    av_frame_free(ref frame);

                    while (avcodec_receive_packet(encCtx, pkt) >= 0)
                    {
                        // MP4: identity rescale 1/sr -> 1/sr.
                        // MKV: rescale from 1/sampleRate to 1/1000 (ms)
                        //      to match the video stream timebase.
                        if (isMp4)
                            ffh_packet_rescale_ts(pkt, 1, sampleRate, 1, sampleRate);
                        else
                            ffh_packet_rescale_ts(pkt, 1, sampleRate, 1, 1000);
                        ffh_set_packet_stream_index(pkt, outStreamIdx);
                        av_interleaved_write_frame(outFmtCtx, pkt);
                        av_packet_unref(pkt);
                    }
                }

                // Flush
                avcodec_send_frame(encCtx, IntPtr.Zero);
                while (avcodec_receive_packet(encCtx, pkt) >= 0)
                {
                    if (isMp4)
                        ffh_packet_rescale_ts(pkt, 1, sampleRate, 1, sampleRate);
                    else
                        ffh_packet_rescale_ts(pkt, 1, sampleRate, 1, 1000);
                    ffh_set_packet_stream_index(pkt, outStreamIdx);
                    av_interleaved_write_frame(outFmtCtx, pkt);
                    av_packet_unref(pkt);
                }
            }
            finally
            {
                av_packet_free(ref pkt);
            }
        }

        // =====================================================================
        // YUV plane copy
        // =====================================================================

        private static void CopyYuvPlanes(
            IntPtr src, IntPtr dst, int width, int height)
        {
            CopyPlane(src, dst, 0, width, height);      // Y
            CopyPlane(src, dst, 1, width / 2, height / 2); // Cb
            CopyPlane(src, dst, 2, width / 2, height / 2); // Cr
        }

        private static void CopyPlane(
            IntPtr src, IntPtr dst, int plane, int width, int height)
        {
            IntPtr srcData = ffh_get_frame_data(src, plane);
            IntPtr dstData = ffh_get_frame_data(dst, plane);
            int srcStride = ffh_get_frame_linesize(src, plane);
            int dstStride = ffh_get_frame_linesize(dst, plane);

            byte[] row = new byte[width];
            for (int y = 0; y < height; y++)
            {
                Marshal.Copy(srcData + y * srcStride, row, 0, width);
                Marshal.Copy(row, 0, dstData + y * dstStride, width);
            }
        }

        // =====================================================================
        // Audio frame fill (s16 interleaved → fltp planar)
        // =====================================================================

        private static void FillAudioFrame(
            IntPtr frame, byte[] wavBytes, int wavOffset,
            int samples, int channels, int frameSize)
        {
            for (int ch = 0; ch < channels; ch++)
            {
                IntPtr plane = ffh_get_frame_data(frame, ch);
                for (int s = 0; s < frameSize; s++)
                {
                    float f = 0f;
                    if (s < samples)
                    {
                        int off = wavOffset + (s * channels + ch) * 2;
                        if (off + 1 < wavBytes.Length)
                        {
                            short sample = (short)(wavBytes[off]
                                | (wavBytes[off + 1] << 8));
                            f = sample / 32768.0f;
                        }
                    }
                    byte[] fb = BitConverter.GetBytes(f);
                    Marshal.Copy(fb, 0, plane + s * 4, 4);
                }
            }
        }

        // =====================================================================
        // WAV helpers
        // =====================================================================

        private static void ParseWavHeader(
            byte[] wav, out int channels, out int sampleRate)
        {
            channels = wav[22] | (wav[23] << 8);
            sampleRate = wav[24] | (wav[25] << 8)
                       | (wav[26] << 16) | (wav[27] << 24);
        }

        private static int FindWavDataOffset(byte[] wav)
        {
            int p = 12;
            while (p + 8 <= wav.Length)
            {
                string id = "" + (char)wav[p] + (char)wav[p + 1]
                               + (char)wav[p + 2] + (char)wav[p + 3];
                int sz = wav[p + 4] | (wav[p + 5] << 8)
                       | (wav[p + 6] << 16) | (wav[p + 7] << 24);
                if (id == "data") return p + 8;
                p += 8 + sz + (sz & 1);
            }
            return 44;
        }

        // =====================================================================
        // Utility
        // =====================================================================

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}