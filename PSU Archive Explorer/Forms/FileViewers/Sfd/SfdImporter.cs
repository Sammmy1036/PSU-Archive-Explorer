using System;
using System.Diagnostics;
using System.IO;

namespace psu_archive_explorer
{
    /// <summary>
    /// Imports an MP4 or MKV file back into a Sofdec .sfd container.
    ///
    /// Pipeline:
    ///   1. Read the original SFD to get the target resolution, framerate,
    ///      and ADX audio parameters (used as the encoder template).
    ///   2. Use FFmpeg to re-encode the input video as a raw MPEG-1 ES,
    ///      scaled back to the original SFD resolution.
    ///   3. Use FFmpeg to decode the input audio to a PCM WAV.
    ///   4. Encode the WAV to ADX using AdxEncoder (with the original ADX
    ///      as the template so channel count / sample rate / highpass match).
    ///   5. Mux video ES + ADX into a new SFD using SofdecMuxer.
    ///
    /// The output SFD is a drop-in replacement for the original file.
    /// No encryption is applied — PSU's Sofdec decoder accepts plaintext
    /// ADX (flags = 0x00) without any issues.
    ///
    /// FFmpeg must be available as ffmpeg.exe next to the application or
    /// on the system PATH.
    /// </summary>
    internal static class SfdImporter
    {
        /// <summary>
        /// Import a video file as a replacement SFD, matching the original's
        /// resolution, framerate, and audio parameters exactly.
        /// </summary>
        /// <param name="inputVideoPath">Path to the source MP4 or MKV file.</param>
        /// <param name="originalSfdBytes">
        ///     Bytes of the original SFD being replaced. Used to extract the
        ///     target resolution, framerate, and ADX audio template.
        /// </param>
        /// <param name="outputSfdPath">Destination path for the new .sfd file.</param>
        public static void ImportToSfd(string inputVideoPath,
                                       byte[] originalSfdBytes,
                                       string outputSfdPath)
        {
            if (!File.Exists(inputVideoPath))
                throw new FileNotFoundException("Input video not found.", inputVideoPath);
            if (originalSfdBytes == null || originalSfdBytes.Length == 0)
                throw new ArgumentException("Original SFD bytes are empty.",
                    nameof(originalSfdBytes));

            // ---- 1. Demux the original SFD to get parameters ----
            var demux = new SofdecDemuxer();
            demux.Parse(originalSfdBytes);

            byte[] origVideoEs = demux.GetVideoPayload();
            byte[] origAdxPayload = demux.GetAdxPayload();

            if (origVideoEs.Length < 12)
                throw new InvalidDataException(
                    "Could not find a video stream in the original SFD.");

            // Read width, height, and framerate from the original MPEG-1 ES.
            double framerate;
            int origWidth, origHeight;
            using (var origDecoder = new Mpeg1Decoder(origVideoEs))
            {
                origWidth = origDecoder.Width;
                origHeight = origDecoder.Height;
                framerate = origDecoder.Framerate > 0
                           ? origDecoder.Framerate
                           : 25.0;
            }

            // Rebuild the original ADX file (with header) to use as the
            // AdxEncoder template. This gives us channel count, sample rate,
            // and highpass frequency automatically.
            bool hasAudio = origAdxPayload.Length > 4
                         && origAdxPayload[0] == 0x80
                         && origAdxPayload[1] == 0x00;

            byte[] adxTemplate = null;
            if (hasAudio)
            {
                adxTemplate = BuildAdxHeader(origAdxPayload, demux.Channels, demux.SampleRate);
            }

            // ---- 2 & 3. Run FFmpeg to produce MPEG-1 ES and PCM WAV ----
            // Use Path.GetRandomFileName() to avoid the double-extension problem
            // that Path.GetTempFileName() causes (.tmp.m1v confuses FFmpeg's
            // format detection). GetRandomFileName gives a clean single name
            // with no extension which we then give the correct extension.
            string tmpDir = Path.GetTempPath();
            string tmpMpeg1 = Path.Combine(tmpDir, Path.GetRandomFileName().Replace(".", "") + ".mpg");
            string tmpWav = Path.Combine(tmpDir, Path.GetRandomFileName().Replace(".", "") + ".wav");

            try
            {
                // Video: scale back to the original SFD resolution and encode
                // as a raw MPEG-1 video elementary stream.
                // -f mpeg1video explicitly tells FFmpeg the output format so it
                // never has to guess from the file extension.
                // -q:v 3 gives near-original quality (1=best, 31=worst).
                string videoArgs =
                    $"-y -i \"{inputVideoPath}\" " +
                    $"-vf scale={origWidth}:{origHeight} " +
                    $"-c:v mpeg1video -q:v 3 -f mpeg1video " +
                    $"-an \"{tmpMpeg1}\"";
                RunFFmpeg(videoArgs);

                // Audio: decode to 16-bit PCM WAV (AdxEncoder requires PCM16).
                // -f wav explicitly specifies the output format.
                if (hasAudio)
                {
                    string audioArgs =
                        $"-y -i \"{inputVideoPath}\" " +
                        $"-vn -c:a pcm_s16le -f wav \"{tmpWav}\"";
                    RunFFmpeg(audioArgs);
                }

                // ---- 4. Encode WAV → ADX ----
                byte[] adxBytes = null;
                if (hasAudio && File.Exists(tmpWav))
                {
                    byte[] wavBytes = File.ReadAllBytes(tmpWav);
                    adxBytes = AdxEncoder.EncodeFromWav(wavBytes, adxTemplate);
                }

                // ---- 5. Read MPEG-1 ES and mux into SFD ----
                byte[] mpeg1Es = File.ReadAllBytes(tmpMpeg1);
                if (mpeg1Es.Length == 0)
                    throw new InvalidDataException(
                        "FFmpeg produced an empty MPEG-1 video stream. " +
                        "Check that the input video file contains a valid video track.");

                byte[] sfdBytes = SofdecMuxer.Mux(
                    mpeg1Es,
                    adxBytes,   // null if no audio — muxer omits audio PES packets
                    framerate);

                File.WriteAllBytes(outputSfdPath, sfdBytes);
            }
            finally
            {
                TryDelete(tmpMpeg1);
                TryDelete(tmpWav);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds a minimal ADX file header around a raw headerless ADX
        /// payload so AdxEncoder.EncodeFromWav has a valid template to read.
        /// Mirrors BuildAdxWithHeader in SfdPreviewPanel.
        /// </summary>
        private static byte[] BuildAdxHeader(byte[] payload, int channels, int sampleRate)
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
            adx[4] = 0x03;               // encoding type
            adx[5] = BlockSize;
            adx[6] = 4;                  // bit depth
            adx[7] = (byte)channels;
            adx[8] = (byte)(sampleRate >> 24);
            adx[9] = (byte)(sampleRate >> 16);
            adx[10] = (byte)(sampleRate >> 8);
            adx[11] = (byte)(sampleRate & 0xFF);
            adx[12] = (byte)(totalSamples >> 24);
            adx[13] = (byte)(totalSamples >> 16);
            adx[14] = (byte)(totalSamples >> 8);
            adx[15] = (byte)(totalSamples & 0xFF);
            adx[16] = 0x01; adx[17] = 0xF4;   // highpass 500 Hz (PSU standard)
            adx[18] = 0x03; adx[19] = 0x00;   // version 3, flags unencrypted

            byte[] cri = System.Text.Encoding.ASCII.GetBytes("(c)CRI");
            Array.Copy(cri, 0, adx, CopyrightOffset + 2, 6);
            Buffer.BlockCopy(payload, 0, adx, AudioStart, usableBytes);
            return adx;
        }

        private static void RunFFmpeg(string args)
        {
            string localFfmpeg = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe");
            string ffmpegExe = File.Exists(localFfmpeg) ? localFfmpeg : FindOnPath("ffmpeg.exe");

            if (ffmpegExe == null)
                throw new InvalidOperationException(
                    "To import this file to SFD, ffmpeg.exe is required.\n\n" +
                    "Please download ffmpeg.exe and either place it in the same folder " +
                    "as this application, or add it to your system PATH.\n\n" +
                    "You can download from: https://ffmpeg.org/download.html");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };

            string stderr;
            int exitCode;

            try
            {
                using (Process proc = Process.Start(psi))
                {
                    if (proc == null)
                        throw new InvalidOperationException(
                            "Process.Start returned null — could not launch ffmpeg.exe.");
                    stderr = proc.StandardError.ReadToEnd();
                    proc.WaitForExit();
                    exitCode = proc.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not launch ffmpeg.exe: {ex.Message}\n\n" +
                    "Make sure ffmpeg.exe is in the application folder or on your PATH.", ex);
            }

            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"ffmpeg exited with code {exitCode}.\n\n" + stderr.Trim());
        }

        private static string FindOnPath(string exeName)
        {
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            foreach (string dir in pathEnv.Split(Path.PathSeparator))
            {
                try
                {
                    string full = Path.Combine(dir.Trim(), exeName);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }
    }
}