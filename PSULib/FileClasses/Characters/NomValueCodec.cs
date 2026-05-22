using System;
using System.Collections.Generic;

namespace PSULib.FileClasses.Characters
{
    /// <summary>
    /// 
    /// NomFile.convertValue decodes a stored <see cref="short"/> into a float.
    /// It is a custom sign-magnitude minifloat: the short's bit pattern is
    /// shifted left 13, an exponent field (0x0F800000) and mantissa field
    /// (0x007FE000) are masked out, the exponent is re-biased into IEEE-754
    /// single range by adding a constant, and the result is multiplied by the
    /// sign of the *signed* short.
    ///
    /// That decode is NOT cleanly invertible as a formula: a negative short is
    /// masked in its two's-complement form, so a negative value does not decode
    /// to the negation of its positive counterpart (e.g. short 1024 decodes to
    /// +3.05e-05 but short -1024 decodes to -32768.0). Rather than chase the
    /// algebraic inverse and its edge cases, exploit the fact that the input
    /// domain is only 65,536 values. Decodes all of them once into a sorted
    /// table and encodes by nearest match.
    ///
    /// Guarantees
    /// ----------
    ///  * For any float that was itself produced by decoding a real NOM.
    ///
    ///  * Fit for novel floats introduced by a user edited GLB, with
    ///    resolution ~0.0122% of range (rotation worst case ~5e-4 on a [-2,2]
    ///    component).
    ///
    /// Two independent tables exist because the exponent bias differs:
    /// 0x37800000 for position data, 0x30000000 for rotation data.
    /// </summary>
    public static class NomValueCodec
    {
        private const int PositionBias = 0x37800000;
        private const int RotationBias = 0x30000000;

        /// <summary>
        /// Decode a stored short to a float. Bit-for-bit identical to
        /// NomFile.convertValue — kept here so encode and decode live together
        /// and the self-test can exercise both. If convertValue is ever made
        /// public this can simply delegate to it.
        /// </summary>
        public static float Decode(short initialValue, bool isRotation)
        {
            int finalAddition = isRotation ? RotationBias : PositionBias;

            int signum = Math.Sign(initialValue);
            int shifted = (initialValue & 0xFFFF) << 13;
            int exponentField = shifted & 0x0F800000;

            if (exponentField == 0)
                return 0.0f;

            int mantissaField = shifted & 0x007FE000;
            int finalBits = (exponentField + finalAddition) | mantissaField;
            return signum * BitConverter.ToSingle(BitConverter.GetBytes(finalBits), 0);
        }

        // Lazily-built encode tables. Each is the set of all 65,536 decoded
        // values, sorted ascending, paired with the short that produced each.
        // Built once on first use; cheap (a few hundred KB) and thread-safe via
        // the double-checked lock below.
        private static float[] _posKeys, _rotKeys;
        private static short[] _posShorts, _rotShorts;
        private static readonly object _buildLock = new object();

        private static void EnsureTable(bool isRotation)
        {
            if ((isRotation ? _rotKeys : _posKeys) != null) return;
            lock (_buildLock)
            {
                if ((isRotation ? _rotKeys : _posKeys) != null) return;

                var pairs = new List<KeyValuePair<float, short>>(65536);
                for (int u = 0; u < 65536; u++)
                {
                    short s = unchecked((short)u);
                    pairs.Add(new KeyValuePair<float, short>(Decode(s, isRotation), s));
                }
                pairs.Sort((a, b) => a.Key.CompareTo(b.Key));

                var keys = new float[65536];
                var shorts = new short[65536];
                for (int i = 0; i < 65536; i++)
                {
                    keys[i] = pairs[i].Key;
                    shorts[i] = pairs[i].Value;
                }

                if (isRotation) { _rotKeys = keys; _rotShorts = shorts; }
                else { _posKeys = keys; _posShorts = shorts; }
            }
        }

        /// <summary>
        /// Encode a float to the stored short whose decoded value is closest.
        /// Exact for values originating from real NOM data; nearest-fit for
        /// novel values. NaN/Infinity are clamped to the table's finite range.
        /// </summary>
        public static short Encode(float value, bool isRotation)
        {
            EnsureTable(isRotation);
            float[] keys = isRotation ? _rotKeys : _posKeys;
            short[] shorts = isRotation ? _rotShorts : _posShorts;

            if (float.IsNaN(value)) value = 0f;
            if (float.IsPositiveInfinity(value)) return shorts[keys.Length - 1];
            if (float.IsNegativeInfinity(value)) return shorts[0];

            // Binary search for the insertion point, then compare the
            // bracketing entries (and their neighbours) for the true nearest.
            int lo = 0, hi = keys.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (keys[mid] < value) lo = mid + 1;
                else hi = mid;
            }

            int bestIdx = lo;
            float bestDist = Math.Abs(keys[lo] - value);
            for (int j = lo - 1; j <= lo + 1; j++)
            {
                if (j < 0 || j >= keys.Length) continue;
                float d = Math.Abs(keys[j] - value);
                if (d < bestDist) { bestDist = d; bestIdx = j; }
            }
            return shorts[bestIdx];
        }

        /// <summary>
        /// Verify the codec in the current environment. Returns true if every
        /// one of the 65,536 shorts, decoded then re-encoded then decoded
        /// again, yields the identical float — for both modes. Call this once
        /// (e.g. in a debug build or a unit test) before relying on the writer.
        /// </summary>
        public static bool SelfTest(out string report)
        {
            int posFail = 0, rotFail = 0;
            for (int u = 0; u < 65536; u++)
            {
                short s = unchecked((short)u);

                float fp = Decode(s, false);
                if (Decode(Encode(fp, false), false) != fp) posFail++;

                float fr = Decode(s, true);
                if (Decode(Encode(fr, true), true) != fr) rotFail++;
            }
            report = "NomValueCodec self-test — position failures: " + posFail +
                     "/65536, rotation failures: " + rotFail + "/65536";
            return posFail == 0 && rotFail == 0;
        }
    }
}