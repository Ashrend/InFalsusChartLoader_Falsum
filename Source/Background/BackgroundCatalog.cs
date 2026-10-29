using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The in-play backgrounds built for custom songs.</summary>
    internal static class BackgroundCatalog
    {
        /// <summary>One difficulty's background: a built material, or a clip still to be started.</summary>
        internal struct Entry
        {
            /// <summary>The material of a still, or of a clip that is already playing.</summary>
            internal IntPtr Material;

            /// <summary>The file of a clip — null for a still, and for "nothing was said".</summary>
            internal string Video;

            /// <summary>Whether the author named anything at all for this difficulty.</summary>
            internal bool Said => Material != IntPtr.Zero || Video != null;
        }

        /// <summary>Per song, one entry per difficulty. Absent songs have no row at all.</summary>
        private static readonly Dictionary<string, Entry[]> ByBase =
            new Dictionary<string, Entry[]>(StringComparer.Ordinal);

        internal static int Count => ByBase.Count;

        /// <summary>Builds the stills for one chart folder.</summary>
        internal static bool Add(ChartInfo info, out string reason)
        {
            reason = null;
            if (info.BackgroundPaths == null) return true;   // the game's own, by request

            var entries = new Entry[ChartInfo.Difficulties];
            bool all = true;

            for (int d = 0; d < entries.Length; d++)
            {
                string path = info.BackgroundPaths[d];
                if (path == null) continue;

                if (IsVideo(path))
                {
                    entries[d].Video = path;
                    continue;
                }

                IntPtr material = JacketFactory.Build(path, "background", out string why);
                if (material == IntPtr.Zero)
                {
                    all = false;
#if DEBUG
                    if (reason == null) reason = $"difficulty {d}: {why}";
#else
                    _ = why;
#endif
                    continue;
                }

                entries[d].Material = material;
            }

            ByBase[info.BaseName] = entries;
            return all;
        }

        /// <summary>Finds a song's background for a difficulty.</summary>
        internal static bool Find(IntPtr songInfo, int difficulty, out Entry entry, out string why)
        {
            entry = default;
            why = null;

            string baseName = JacketCatalog.NameOf(songInfo);
            if (baseName == null) return false;
            if (!ByBase.TryGetValue(baseName, out Entry[] entries)) return false;

            if (difficulty >= 0 && difficulty < entries.Length)
            {
                entry = entries[difficulty];
            }
            else
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (i == 0) entry = entries[0];
                    else if (entries[i].Material != entry.Material || entries[i].Video != entry.Video)
                    {
#if DEBUG
                        why = "a background is set per difficulty and the selected one could not be read";
#endif
                        return false;
                    }
                }

                if (!entry.Said) return false;
            }

            if (!entry.Said) return false;

            if (entry.Video == null) return true;

            IntPtr material = VideoBackground.Material(entry.Video, out why);
            if (material == IntPtr.Zero) return false;

            entry.Material = material;
            return true;
        }

        /// <summary>Whether a background path is a clip.</summary>
        private static bool IsVideo(string path) =>
            string.Equals(System.IO.Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase);
    }
}
