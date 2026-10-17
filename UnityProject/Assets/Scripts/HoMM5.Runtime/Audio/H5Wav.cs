using System;

namespace HoMM5.Audio
{
    /// <summary>
    /// Decodes the RIFF/WAVE files HoMM5 ships in <c>soundsfx/</c>.
    ///
    /// Almost every effect is IMA ADPCM (format 17, 1911 of 2046 files); the rest
    /// are plain PCM (format 1). Unity's own WAV importer does not read the ADPCM
    /// variant, so the decoder lives here and hands Unity float samples through
    /// <c>AudioClip.SetData</c>.
    ///
    /// The ADPCM path mirrors the reference decoder that was checked against the
    /// shipped files: mono output matches the reference bit for bit, stereo matches
    /// per channel, and the frame count is trimmed to the <c>fact</c> chunk. Nibbles
    /// are read low-nibble first, which is the WAV convention.
    /// </summary>
    public static class H5Wav
    {
        public sealed class Decoded
        {
            /// <summary>Interleaved samples in -1..1, exactly Frames * Channels long.</summary>
            public float[] Samples;
            public int Channels;
            public int SampleRate;
            public int Frames => Channels > 0 ? Samples.Length / Channels : 0;
        }

        static readonly int[] StepTable =
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230,
            253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963,
            1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327,
            3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845, 8630, 9493, 10442,
            11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
            32767,
        };

        static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8 };

        const int FormatPcm = 1;
        const int FormatFloat = 3;
        const int FormatImaAdpcm = 17;

        /// <summary>Decode a whole file. Returns null when it is not a WAV this decoder reads.</summary>
        public static Decoded Decode(byte[] data)
        {
            if (data == null || data.Length < 12) return null;
            if (!HasTag(data, 0, "RIFF") || !HasTag(data, 8, "WAVE")) return null;

            long fmtBody = -1, fmtLen = 0, dataBody = -1, dataLen = 0, factFrames = 0;
            long p = 12;
            while (p + 8 <= data.Length)
            {
                var len = ReadU32(data, (int)p + 4);
                var body = p + 8;
                if (body > data.Length) break;
                if (HasTag(data, (int)p, "fmt ")) { fmtBody = body; fmtLen = len; }
                else if (HasTag(data, (int)p, "fact") && len >= 4) factFrames = ReadU32(data, (int)body);
                else if (HasTag(data, (int)p, "data"))
                {
                    dataBody = body;
                    dataLen = Math.Min(len, data.Length - body);
                }
                p = body + len + (len & 1);
            }
            if (fmtBody < 0 || dataBody < 0 || fmtLen < 16) return null;

            var fo = (int)fmtBody;
            var format = ReadU16(data, fo);
            var channels = ReadU16(data, fo + 2);
            var rate = (int)ReadU32(data, fo + 4);
            var align = ReadU16(data, fo + 12);
            var bits = ReadU16(data, fo + 14);
            if (channels <= 0 || rate <= 0) return null;

            float[] samples;
            if (format == FormatPcm || format == FormatFloat)
            {
                samples = DecodePcm(data, (int)dataBody, (int)dataLen, format, bits, channels);
            }
            else if (format == FormatImaAdpcm)
            {
                // wSamplesPerBlock sits after cbSize, present when the fmt chunk has 20+ bytes.
                var spb = fmtLen >= 20 ? ReadU16(data, fo + 18) : 0;
                samples = DecodeImaAdpcm(data, (int)dataBody, (int)dataLen, channels, align, spb, factFrames);
            }
            else
            {
                return null;
            }
            if (samples == null) return null;
            return new Decoded { Samples = samples, Channels = channels, SampleRate = rate };
        }

        // -- PCM --------------------------------------------------------------

        static float[] DecodePcm(byte[] data, int start, int length, int format, int bits, int channels)
        {
            var validInt = format == FormatPcm && (bits == 8 || bits == 16 || bits == 24 || bits == 32);
            var validFloat = format == FormatFloat && bits == 32;
            if (!validInt && !validFloat) return null;

            var bytesPer = bits / 8;
            var count = length / bytesPer;
            count -= count % channels;
            var s = new float[count];
            for (var i = 0; i < count; i++)
            {
                var o = start + i * bytesPer;
                float v;
                if (validFloat)
                {
                    v = BitConverter.ToSingle(data, o);
                }
                else if (bits == 8)
                {
                    v = (data[o] - 128) / 128f;
                }
                else if (bits == 16)
                {
                    v = (short)(data[o] | (data[o + 1] << 8)) / 32768f;
                }
                else if (bits == 24)
                {
                    var x = data[o] | (data[o + 1] << 8) | (data[o + 2] << 16);
                    if ((x & 0x800000) != 0) x -= 0x1000000;
                    v = x / 8388608f;
                }
                else
                {
                    v = BitConverter.ToInt32(data, o) / 2147483648f;
                }
                s[i] = v;
            }
            return s;
        }

        // -- IMA ADPCM --------------------------------------------------------

        /// <summary>
        /// Each block starts with a 4-byte header per channel (int16 predictor,
        /// byte step index, reserved), whose predictor is the block's first sample.
        /// After the headers the nibbles come in 4-byte groups per channel, eight
        /// samples per group, groups repeating in channel order.
        /// </summary>
        static float[] DecodeImaAdpcm(byte[] data, int start, int length, int channels, int align,
                                      int samplesPerBlock, long factFrames)
        {
            if (align < 4 * channels) return null;
            var spb = samplesPerBlock > 0 ? samplesPerBlock : (align - 4 * channels) * 8 / (4 * channels) + 1;
            var blocks = (length + align - 1) / align;
            var buf = new float[blocks * spb * channels];
            var written = 0;

            for (var b = 0; b < blocks; b++)
            {
                var bs = start + b * align;
                var blockBytes = Math.Min(align, start + length - bs);
                if (blockBytes < 4 * channels) break;

                var framesThis = Math.Min(spb, 1 + ((blockBytes - 4 * channels) / (4 * channels)) * 8);
                var f0 = written;
                for (var c = 0; c < channels; c++)
                {
                    int pred = (short)(data[bs + 4 * c] | (data[bs + 4 * c + 1] << 8));
                    var idx = Clamp(data[bs + 4 * c + 2], 0, 88);
                    buf[f0 * channels + c] = pred / 32768f;

                    for (var f = 1; f < framesThis; f++)
                    {
                        var k = f - 1;
                        var chunk = k / 8;
                        var within = k % 8;
                        var at = bs + 4 * channels + 4 * channels * chunk + 4 * c + within / 2;
                        var nib = (within & 1) == 0 ? (data[at] & 0x0F) : (data[at] >> 4);

                        var step = StepTable[idx];
                        var diff = step >> 3;
                        if ((nib & 4) != 0) diff += step;
                        if ((nib & 2) != 0) diff += step >> 1;
                        if ((nib & 1) != 0) diff += step >> 2;
                        pred = (nib & 8) != 0 ? pred - diff : pred + diff;
                        pred = Clamp(pred, -32768, 32767);
                        idx = Clamp(idx + IndexTable[nib & 7], 0, 88);

                        buf[(f0 + f) * channels + c] = pred / 32768f;
                    }
                }
                written += framesThis;
            }

            // The last block is padded; the fact chunk says how much of it is real.
            var frames = written;
            if (factFrames > 0 && factFrames < written) frames = (int)factFrames;
            var result = new float[frames * channels];
            Array.Copy(buf, result, result.Length);
            return result;
        }

        // -- byte helpers -------------------------------------------------------

        static bool HasTag(byte[] d, int offset, string tag)
        {
            if (offset < 0 || offset + 4 > d.Length) return false;
            for (var i = 0; i < 4; i++)
                if (d[offset + i] != tag[i]) return false;
            return true;
        }

        static int ReadU16(byte[] d, int o) => d[o] | (d[o + 1] << 8);

        static long ReadU32(byte[] d, int o) =>
            d[o] | (d[o + 1] << 8) | (d[o + 2] << 16) | ((long)d[o + 3] << 24);

        static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
