using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InFalsusChartLoader_falsum
{
    /// <summary>What the reload watches: the chart folder's fingerprint, and the names it holds.</summary>
    /// <remarks>
    /// Everything here is plain managed code: no Unity, no IL2CPP, no <see cref="Diagnostics"/>, so the
    /// rule tests can link this file and cover it without a running game.
    /// </remarks>
    internal static class ReloadPlan
    {
        /// <summary>The extensions that can change what a chart folder means.</summary>
        private static readonly string[] WatchedExtensions =
        {
            ".falsum", ".spc", ".wav", ".mp3", ".ogg", ".png", ".mp4",
        };

        /// <summary>The two files that carry a folder's description and have no extension.</summary>
        private const string InfoFileName = "if";

        private const string PackFileName = "pack";

        /// <summary>Whether a file can change what the loader reads.</summary>
        internal static bool Relevant(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string name = Path.GetFileName(path);
            if (string.Equals(name, InfoFileName, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(name, PackFileName, StringComparison.OrdinalIgnoreCase)) return true;

            string extension = Path.GetExtension(name).ToLowerInvariant();
            return Array.IndexOf(WatchedExtensions, extension) >= 0;
        }

        /// <summary>
        /// One string standing for everything the loader reads under <paramref name="root"/>: each watched
        /// file's path, byte count and last write time. Two equal fingerprints mean nothing changed.
        /// </summary>
        /// <remarks>
        /// Returns false when the folder cannot be read at all. That is deliberately not the same as an
        /// empty folder: treating a failed listing as "everything was deleted" would wipe the custom songs
        /// on a transient I/O error.
        /// </remarks>
        internal static bool TryFingerprint(string root, out string fingerprint)
        {
            var text = new StringBuilder();
            fingerprint = string.Empty;

            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

            string[] files;
            try
            {
                files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            }
            catch (Exception)
            {
                return false;
            }

            // Directory.GetFiles makes no promise about order, and an unstable order would read as a change.
            Array.Sort(files, StringComparer.Ordinal);

            foreach (string path in files)
            {
                if (!Relevant(path)) continue;

                try
                {
                    var file = new FileInfo(path);
                    text.Append(path).Append('|').Append(file.Length).Append('|')
                        .Append(file.LastWriteTimeUtc.Ticks).Append('\n');
                }
                catch (Exception)
                {
                    // A file that vanished between listing and reading is not worth failing the whole scan.
                }
            }

            fingerprint = text.ToString();
            return true;
        }

        /// <summary>How many song names appeared and disappeared between two scans.</summary>
        internal static void Compare(List<string> before, List<string> after, out int added, out int removed)
        {
            var old = new HashSet<string>(before ?? new List<string>(), StringComparer.Ordinal);
            var now = new HashSet<string>(after ?? new List<string>(), StringComparer.Ordinal);

            added = 0;
            removed = 0;

            foreach (string name in now)
                if (!old.Contains(name)) added++;

            foreach (string name in old)
                if (!now.Contains(name)) removed++;
        }
    }
}
