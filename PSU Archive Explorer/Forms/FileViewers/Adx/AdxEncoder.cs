using System;
using System.IO;

namespace psu_archive_explorer
{
    /// <summary>
    /// WAV-to-ADX encoder specialized for Phantasy Star Universe audio files.
    ///
    /// This is the inverse of <see cref="AdxDecoder"/>. It produces a standard
    /// CRI ADX type 0x03 stream with 18 byte blocks and 4 bit samples.
    ///
    /// Design choices (per intended use as a drop-in PSU sound replacer):
    ///   * Channel count, sample rate, and highpass frequency are copied from a
    ///     TEMPLATE ADX (the file being replaced), NOT read from the WAV. The
    ///     WAV supplies only PCM samples; if its channel count differs from the
    ///     template it is down/up-mixed to conform, and if its sample rate
    ///     differs it is resampled to the template's rate (see Resample).
    ///   * Output is always written UNENCRYPTED (flags = 0x00). PSU's type 8
    ///     encryption is optional and the decoder reads plaintext streams fine.
    ///
    /// Because the WAV is resampled to the template's rate, the result always
    /// plays at the correct pitch/speed regardless of the source WAV's rate.
    ///
    /// Encoding is lossy (4 bit ADPCM); WAV -> ADX -> WAV will not round-trip
    /// bit-exactly. This is the same loss the original PSU files already carry.
    /// </summary>
    public static class AdxEncoder
    {
        // PSU always uses 18 byte blocks with 4 bit samples => 32 samples/block.
        private const int BlockSize = 18;
        private const int SamplesPerBlock = 32;

        // ADX header layout. The header fields occupy bytes 0..19:
        //   0-1 magic, 2-3 copyrightOffset, 4 encoding, 5 block size,
        //   6 bit depth, 7 channels, 8-11 sample rate, 12-15 total samples,
        //   16-17 highpass freq, 18 version, 19 flags.
        // The "(c)CRI" string is 6 bytes and must sit AFTER the flags byte,
        // ending exactly at copyrightOffset + 4 (where audio begins). For the
        // string to start at or past byte 20, copyrightOffset must be >= 22;
        // the canonical value is 0x1C, putting "(c)CRI" at bytes 26-31 and
        // audioStart at 0x20 (32). Earlier this used 0x12, which forced the
        // string to overlap the header fields and corrupted the flags byte
        // (the decoder then saw 0x43 = 'C' from "(c)CRI" instead of flags).
        private const int CopyrightOffset = 0x1C;
        private const int AudioStart = CopyrightOffset + 4; // 0x20 = 32

        // Plaintext scale ceiling enforced by the decoder (13 bit).
        private const int MaxScale = 0x1FFF;

        /// <summary>
        /// Encode a PCM16 WAV file into PSU-style ADX bytes, copying stream
        /// parameters (channels / sample rate / highpass) from a template ADX.
        /// </summary>
        /// <param name="wav">A standard PCM16 little-endian WAV file's bytes.</param>
        /// <param name="templateAdx">The original ADX being replaced; supplies params.</param>
        public static byte[] EncodeFromWav(byte[] wav, byte[] templateAdx)
        {
            if (wav == null) throw new ArgumentNullException(nameof(wav));
            if (templateAdx == null) throw new ArgumentNullException(nameof(templateAdx));

            // ---- Read params from the template ADX ----
            if (templateAdx.Length < 20)
                throw new InvalidDataException("Template file too small to be ADX.");
            if (templateAdx[0] != 0x80 || templateAdx[1] != 0x00)
                throw new InvalidDataException("Template is not an ADX file: bad magic.");

            int channels = templateAdx[7];
            if (channels < 1 || channels > 2)
                throw new InvalidDataException(
                    $"Template has unsupported channel count: {channels}.");

            int sampleRate = (templateAdx[8] << 24) | (templateAdx[9] << 16)
                           | (templateAdx[10] << 8) | templateAdx[11];
            if (sampleRate <= 0)
                throw new InvalidDataException("Template has non-positive sample rate.");

            int highpassFreq = (templateAdx[16] << 8) | templateAdx[17];

            // ---- Parse the WAV ----
            WavData src = ParseWav(wav);

            // ---- Conform WAV channels to the template's channel count ----
            // WAV is the source of samples only; the template dictates layout.
            short[][] chan = ConformChannels(src, channels);

            // ---- Resample to the template's sample rate if needed ----
            // The output stream is stamped with the template's rate, so the
            // samples themselves must actually BE at that rate. If the WAV was
            // recorded at a different rate, resample each channel here so the
            // result plays at the correct pitch/speed. (Linear interpolation:
            // more than adequate given the 4 bit ADPCM quantization that
            // follows, and keeps this file dependency-free.)
            if (src.SampleRate != sampleRate && src.SampleRate > 0)
            {
                for (int ch = 0; ch < chan.Length; ch++)
                    chan[ch] = Resample(chan[ch], src.SampleRate, sampleRate);
            }

            int totalSamples = chan[0].Length;
            if (totalSamples <= 0)
                throw new InvalidDataException("WAV contains no samples.");

            // ---- Prediction coefficients (Q12 fixed point) ----
            // Identical formula to AdxDecoder so encode/decode filters match.
            double z = Math.Cos(2.0 * Math.PI * highpassFreq / sampleRate);
            double a = Math.Sqrt(2.0) - z;
            double b = Math.Sqrt(2.0) - 1.0;
            double c = (a - Math.Sqrt((a + b) * (a - b))) / b;
            int coef1 = (int)Math.Floor(c * 2.0 * 4096.0);
            int coef2 = (int)Math.Floor(-(c * c) * 4096.0);

            int blockCount = (totalSamples + SamplesPerBlock - 1) / SamplesPerBlock;
            int frameSize = BlockSize * channels;
            int audioBytes = blockCount * frameSize;

            byte[] adx = new byte[AudioStart + audioBytes];

            // ---- Header ----
            adx[0] = 0x80;
            adx[1] = 0x00;
            adx[2] = (byte)(CopyrightOffset >> 8);
            adx[3] = (byte)(CopyrightOffset & 0xFF);
            adx[4] = 0x03;                       // encoding type (PSU standard)
            adx[5] = (byte)BlockSize;            // 0x12
            adx[6] = 4;                          // sample bit depth
            adx[7] = (byte)channels;
            adx[8] = (byte)(sampleRate >> 24);
            adx[9] = (byte)(sampleRate >> 16);
            adx[10] = (byte)(sampleRate >> 8);
            adx[11] = (byte)(sampleRate);
            adx[12] = (byte)(totalSamples >> 24);
            adx[13] = (byte)(totalSamples >> 16);
            adx[14] = (byte)(totalSamples >> 8);
            adx[15] = (byte)(totalSamples);
            adx[16] = (byte)(highpassFreq >> 8);
            adx[17] = (byte)(highpassFreq & 0xFF);
            adx[18] = 0x04;                      // ADX version (standard)
            adx[19] = 0x00;                      // flags: 0x00 = unencrypted

            // "(c)CRI" copyright string: 6 bytes ending right before AudioStart.
            // With AudioStart = 0x20 this lands at bytes 26-31, safely after the
            // flags byte at offset 19. Bytes 20-25 are left zero (reserved).
            byte[] cri = { (byte)'(', (byte)'c', (byte)')', (byte)'C', (byte)'R', (byte)'I' };
            Array.Copy(cri, 0, adx, AudioStart - cri.Length, cri.Length);

            // ---- Encode each channel independently ----
            for (int ch = 0; ch < channels; ch++)
            {
                short[] samples = chan[ch];

                // Decoder history (reconstructed samples), per channel.
                int hist1 = 0, hist2 = 0;

                for (int bi = 0; bi < blockCount; bi++)
                {
                    int sampleBase = bi * SamplesPerBlock;
                    int samplesThisBlock = Math.Min(SamplesPerBlock, totalSamples - sampleBase);

                    // -- Pass 1: pick a scale for this block --
                    // The scale must be large enough that every residual,
                    // divided by it, lands in the signed 4 bit range -8..+7.
                    // We mirror the decoder by predicting from RECONSTRUCTED
                    // history (not original samples) so the encoder's model of
                    // history stays identical to what the decoder will see.
                    int trialH1 = hist1, trialH2 = hist2;
                    int maxResidual = 0;
                    for (int s = 0; s < samplesThisBlock; s++)
                    {
                        int prediction = (coef1 * trialH1 + coef2 * trialH2) >> 12;
                        int residual = samples[sampleBase + s] - prediction;
                        int mag = Math.Abs(residual);
                        if (mag > maxResidual) maxResidual = mag;

                        // For the trial pass we advance history with the
                        // original sample; pass 2 corrects this with the
                        // genuine reconstructed value once scale is known.
                        trialH2 = trialH1;
                        trialH1 = samples[sampleBase + s];
                    }

                    // scale such that maxResidual / scale <= 7  ->  scale >= max/7
                    int scale = (maxResidual + 6) / 7; // ceil(max / 7)
                    if (scale < 1) scale = 1;
                    if (scale > MaxScale) scale = MaxScale;

                    // -- Pass 2: quantize using scale, with a real mini-decoder --
                    int blockOff = AudioStart + bi * frameSize + BlockSize * ch;

                    // Decoder computes the working scale as (stored & 0x1FFF)+1,
                    // so store scale-1 to round-trip exactly.
                    int storedScale = scale - 1;
                    adx[blockOff] = (byte)((storedScale >> 8) & 0x1F);
                    adx[blockOff + 1] = (byte)(storedScale & 0xFF);

                    for (int s = 0; s < samplesThisBlock; s++)
                    {
                        int prediction = (coef1 * hist1 + coef2 * hist2) >> 12;
                        int residual = samples[sampleBase + s] - prediction;

                        // Round-to-nearest division by scale, then clamp to the
                        // signed nibble range. Truncation here would bias the
                        // signal, so we round.
                        int q = DivRound(residual, scale);
                        if (q > 7) q = 7;
                        else if (q < -8) q = -8;

                        // Reconstruct exactly as AdxDecoder will, and feed the
                        // RECONSTRUCTED value into history. This is the key step
                        // that keeps encoder and decoder in lockstep and stops
                        // error from accumulating across the block.
                        int recon = prediction + q * scale;
                        if (recon > 32767) recon = 32767;
                        else if (recon < -32768) recon = -32768;
                        hist2 = hist1;
                        hist1 = recon;

                        // Pack the 4 bit nibble: even samples -> high nibble.
                        int nibble = q & 0x0F;
                        int byteOff = blockOff + 2 + (s >> 1);
                        if ((s & 1) == 0)
                            adx[byteOff] = (byte)(nibble << 4);
                        else
                            adx[byteOff] |= (byte)nibble;
                    }

                    // Samples beyond samplesThisBlock in the final block stay
                    // zero (silent padding), matching how the decoder ignores
                    // them once sampleIndex reaches totalSamples.
                }
            }

            return adx;
        }

        /// <summary>Round-half-away-from-zero integer division.</summary>
        private static int DivRound(int numerator, int denominator)
        {
            if (denominator <= 0) return 0;
            if (numerator >= 0)
                return (numerator + denominator / 2) / denominator;
            return -((-numerator + denominator / 2) / denominator);
        }

        /// <summary>
        /// Resample a single channel of PCM16 from <paramref name="srcRate"/> to
        /// <paramref name="dstRate"/> using linear interpolation.
        ///
        /// Linear interpolation is chosen deliberately: the encoded output is
        /// 4 bit ADPCM, which discards far more fidelity than the small amount
        /// of high-frequency aliasing a linear resampler introduces. It also
        /// keeps this file free of external resampling dependencies. For the
        /// modest ratios seen in practice (e.g. 44100 -> 48000) the result is
        /// inaudibly different from a higher-order resampler after ADPCM.
        /// </summary>
        private static short[] Resample(short[] input, int srcRate, int dstRate)
        {
            if (input == null || input.Length == 0 || srcRate == dstRate)
                return input;

            // Output length scaled by the rate ratio (round to nearest).
            long outLenL = (long)input.Length * dstRate / srcRate;
            int outLen = (int)Math.Max(1, outLenL);
            short[] output = new short[outLen];

            // Step through the source at fractional increments. For output
            // sample i, the source position is i * srcRate / dstRate; we
            // interpolate between the two neighbouring source samples.
            double ratio = (double)srcRate / dstRate;
            for (int i = 0; i < outLen; i++)
            {
                double srcPos = i * ratio;
                int idx = (int)srcPos;
                double frac = srcPos - idx;

                int s0 = input[idx];
                int s1 = idx + 1 < input.Length ? input[idx + 1] : input[idx];

                int value = (int)Math.Round(s0 + (s1 - s0) * frac);
                if (value > 32767) value = 32767;
                else if (value < -32768) value = -32768;
                output[i] = (short)value;
            }

            return output;
        }

        /// <summary>
        /// Conform parsed WAV samples to the requested channel count.
        ///   stereo -> mono : average of L and R
        ///   mono   -> stereo: duplicate into both channels
        ///   equal          : pass through
        /// Returns one short[] per output channel.
        /// </summary>
        private static short[][] ConformChannels(WavData src, int outChannels)
        {
            if (src.Channels == outChannels)
            {
                short[][] passthrough = new short[outChannels][];
                for (int ch = 0; ch < outChannels; ch++)
                    passthrough[ch] = src.Channel(ch);
                return passthrough;
            }

            if (src.Channels == 2 && outChannels == 1)
            {
                short[] l = src.Channel(0);
                short[] r = src.Channel(1);
                short[] mono = new short[l.Length];
                for (int i = 0; i < mono.Length; i++)
                    mono[i] = (short)((l[i] + r[i]) / 2);
                return new[] { mono };
            }

            if (src.Channels == 1 && outChannels == 2)
            {
                short[] m = src.Channel(0);
                return new[] { (short[])m.Clone(), (short[])m.Clone() };
            }

            throw new NotSupportedException(
                $"Cannot conform {src.Channels}-channel WAV to {outChannels} channels.");
        }

        /// <summary>Minimal parsed PCM16 WAV: interleaved samples + channel count.</summary>
        private sealed class WavData
        {
            public int Channels;
            public int SampleRate;
            public short[] Interleaved; // [frame * Channels + ch]

            public int FrameCount => Interleaved.Length / Channels;

            public short[] Channel(int ch)
            {
                short[] result = new short[FrameCount];
                for (int i = 0; i < result.Length; i++)
                    result[i] = Interleaved[i * Channels + ch];
                return result;
            }
        }

        /// <summary>
        /// Parse a standard PCM16 little-endian WAV. Walks the RIFF chunk list
        /// so it tolerates extra chunks (LIST, fact, etc.) before "data".
        /// </summary>
        private static WavData ParseWav(byte[] wav)
        {
            if (wav.Length < 44)
                throw new InvalidDataException("File too small to be a WAV.");
            if (wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
                throw new InvalidDataException("Not a WAV file: missing RIFF tag.");
            if (wav[8] != 'W' || wav[9] != 'A' || wav[10] != 'V' || wav[11] != 'E')
                throw new InvalidDataException("Not a WAV file: missing WAVE tag.");

            int audioFormat = -1, channels = -1, sampleRate = -1, bitsPerSample = -1;
            int dataOffset = -1, dataLength = -1;

            int p = 12;
            while (p + 8 <= wav.Length)
            {
                string id = "" + (char)wav[p] + (char)wav[p + 1]
                              + (char)wav[p + 2] + (char)wav[p + 3];
                int chunkSize = wav[p + 4] | (wav[p + 5] << 8)
                              | (wav[p + 6] << 16) | (wav[p + 7] << 24);
                int body = p + 8;

                if (id == "fmt ")
                {
                    if (body + 16 > wav.Length)
                        throw new InvalidDataException("Truncated fmt chunk.");
                    audioFormat = wav[body] | (wav[body + 1] << 8);
                    channels = wav[body + 2] | (wav[body + 3] << 8);
                    sampleRate = wav[body + 4] | (wav[body + 5] << 8)
                                  | (wav[body + 6] << 16) | (wav[body + 7] << 24);
                    bitsPerSample = wav[body + 14] | (wav[body + 15] << 8);
                }
                else if (id == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(chunkSize, wav.Length - body);
                }

                // Chunks are word-aligned: odd-sized chunks have a pad byte.
                p = body + chunkSize + (chunkSize & 1);
            }

            if (audioFormat == -1)
                throw new InvalidDataException("WAV has no fmt chunk.");
            if (audioFormat != 1)
                throw new NotSupportedException(
                    $"WAV audio format {audioFormat} not supported (need PCM = 1). " +
                    "Convert the file to uncompressed 16 bit PCM first.");
            if (bitsPerSample != 16)
                throw new NotSupportedException(
                    $"WAV bit depth {bitsPerSample} not supported (need 16 bit).");
            if (channels < 1 || channels > 2)
                throw new NotSupportedException(
                    $"WAV channel count {channels} not supported (need mono or stereo).");
            if (dataOffset == -1)
                throw new InvalidDataException("WAV has no data chunk.");

            int sampleCount = dataLength / 2;
            // Drop a dangling sample if data length isn't a whole number of frames.
            sampleCount -= sampleCount % channels;

            short[] interleaved = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                int o = dataOffset + i * 2;
                interleaved[i] = (short)(wav[o] | (wav[o + 1] << 8));
            }

            return new WavData
            {
                Channels = channels,
                SampleRate = sampleRate,
                Interleaved = interleaved
            };
        }
    }
}