using System;
using System.IO;

namespace InFalsusChartLoader
{
    /// <summary>Turns a plain audio file into the bytes the game's reader expects.</summary>
    internal static class AudioCodec
    {
        private const ulong Gold = 0x9E3779B97F4A7C15;
        private const ulong Mul1 = 0xD6E8FEB86659FD93;
        private const ulong Mul2 = 0xA24BAED4963EE407;

        /// <summary>A song's audio with the game's keystream applied.</summary>
        internal sealed class Prepared
        {
            internal byte[] Bytes;

            /// <summary>The length the game must be told the file is, so its read window matches.</summary>
            internal int Length => Bytes.Length;
        }

        /// <summary>Reads <paramref name="path"/> and returns it transformed, or null with a reason.</summary>
        internal static Prepared Load(string path, out string reason)
        {
            reason = null;

            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{Path.GetFileName(path)} could not be read: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return null;
            }

            if (bytes.Length == 0)
            {
#if DEBUG
                reason = $"{Path.GetFileName(path)} is empty";
#endif
                return null;
            }

            bytes = SkipTags(bytes);
            if (bytes.Length == 0)
            {
#if DEBUG
                reason = $"{Path.GetFileName(path)} holds only tag data";
#endif
                return null;
            }

            ulong seed = unchecked(Gold * (uint)bytes.Length);

            int i = 0;
            while (i < bytes.Length)
            {
                long position = i;
                ulong word = Word(position >> 3, seed);

                int groupEnd = (int)Math.Min(((position >> 3) + 1) << 3, bytes.Length);
                for (; i < groupEnd; i++)
                    bytes[i] ^= (byte)(word >> (int)(8 * ((long)i & 7)));
            }

            return new Prepared { Bytes = bytes };
        }

        /// <summary>Drops the tags the game's reader would otherwise treat as audio.</summary>
        private static byte[] SkipTags(byte[] bytes)
        {
            int start = 0;
            int end = bytes.Length;

            if (end >= 10 && bytes[0] == 'I' && bytes[1] == 'D' && bytes[2] == '3')
            {
                int version = bytes[3];
                int size = version == 2
                    ? ((bytes[6] & 0x7F) << 14) | ((bytes[7] & 0x7F) << 7) | (bytes[8] & 0x7F)
                    : ((bytes[6] & 0x7F) << 21) | ((bytes[7] & 0x7F) << 14) |
                      ((bytes[8] & 0x7F) << 7) | (bytes[9] & 0x7F);

                int total = 10 + size;
                if (version >= 4 && (bytes[5] & 0x10) != 0) total += 10;
                if (total > 0 && total <= end) start = total;
            }

            if (end - start >= 128 &&
                bytes[end - 128] == 'T' && bytes[end - 127] == 'A' && bytes[end - 126] == 'G')
                end -= 128;

            if (start == 0 && end == bytes.Length) return bytes;

            var audio = new byte[end - start];
            Buffer.BlockCopy(bytes, start, audio, 0, audio.Length);
            return audio;
        }

        /// <summary>One keystream word.</summary>
        private static ulong Word(long n, ulong seed)
        {
            ulong t = unchecked(Mul1 * (ulong)n + seed);
            ulong u = t ^ Ror(t, 47);
            ulong v = unchecked(Mul2 * u);
            return v ^ Ror(v, 23);
        }

        private static ulong Ror(ulong x, int r) => (x >> r) | (x << (64 - r));
    }
}
