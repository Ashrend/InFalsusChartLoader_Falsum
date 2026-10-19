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
