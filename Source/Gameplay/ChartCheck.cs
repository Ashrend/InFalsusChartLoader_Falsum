using System;
using System.IO;

namespace InFalsusChartLoader_falsum
{
    /// <summary>Decodes one difficulty with the game's own decoder.</summary>
    /// <remarks>
    /// The tuple is the game's 32-byte chart record; the decoder fills it in. The seam exists so
    /// that the two places that check a chart — the background preparation and the import — go
    /// through one implementation and cannot drift apart.
    /// </remarks>
    internal delegate bool ChartDecoder(byte[] bytes, string seed, IntPtr tuple, out string reason);

    /// <summary>Checks one difficulty of one song.</summary>
    internal static unsafe class ChartCheck
    {
        /// <summary>Checks one difficulty, and turns a decoder that throws into a refusal.</summary>
        /// <remarks>
        /// A chart is user data, and the decoder is the game's own code reached through a native
        /// call: a file that is not what it claims to be can make it throw. That is a refusal of
        /// this difficulty, not a crash — the song is skipped, and the frame loop does not come
        /// back to it on every frame.
        /// </remarks>
        /// <param name="info">The song the difficulty belongs to.</param>
        /// <param name="difficulty">The difficulty to check.</param>
        /// <param name="decode">The decoder to check it with.</param>
        /// <param name="reason">Why the difficulty was refused, or null when it was accepted.</param>
        /// <returns>True when the difficulty was accepted.</returns>
        internal static bool Try(ChartInfo info, int difficulty, ChartDecoder decode, out string reason)
        {
            reason = null;

            if (info.NoChart[difficulty]) return true;

            string path = info.ChartPaths[difficulty];
            string seed = info.ChartNames[difficulty];

            byte[] bytes = info.ChartBytes != null ? info.ChartBytes[difficulty] : null;
            if (bytes == null)
            {
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (Exception e)
                {
                    reason = $"difficulty {difficulty} could not be read: {Diagnostics.Describe(e)}";
                    return false;
                }
            }

            byte[] scratch = new byte[32];       // the tuple the decoder fills in
            try
            {
                fixed (byte* tuple = scratch)
                {
                    if (decode(bytes, seed, (IntPtr)tuple, out string why)) return true;

                    reason = $"difficulty {difficulty}: {why}";
                    return false;
                }
            }
            catch (Exception e)
            {
                reason = $"difficulty {difficulty}: {Diagnostics.Describe(e)}";
                return false;
            }
        }
    }
}
