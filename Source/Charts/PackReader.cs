using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InFalsusChartLoader_falsum
{
    /// <summary>Reads one `[Pack]xxx` directory: the pack manifest and its song folders.</summary>
    internal static class PackReader
    {
        private const string ManifestName = "pack";

        private const string CoverName = "pack_cover.png";

        internal static bool TryRead(string folder, string logicalId,
                                     Func<ChartInfo, string> validateCharts,
                                     out ChartPack pack, out string reason)
        {
            pack = null;
            reason = null;

            string manifestPath = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifestPath))
            {
                reason = "pack file not found";
                return false;
            }

            string json;
            try
            {
                json = File.ReadAllText(manifestPath, Encoding.UTF8);
            }
            catch (Exception e)
            {
                reason = "pack file could not be read: " + Diagnostics.Describe(e);
                return false;
            }

            if (!PackManifest.TryParse(json, out PackManifest manifest, out reason))
                return false;

            string cover = Path.Combine(folder, CoverName);
            if (!File.Exists(cover)) cover = null;

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(folder);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                reason = "pack folder could not be listed: " + Diagnostics.Describe(e);
                return false;
            }

            var discovered = new ChartPack
            {
                Folder = folder,
                LogicalId = logicalId,
                Names = manifest.Names,
                Category = manifest.Category,
                CoverPath = cover,
            };

            foreach (string child in folders)
            {
                string name = Path.GetFileName(child);

                if (!ChartInfoReader.TryRead(child, out ChartInfo info, out string why))
                {
                    Diagnostics.Warn($"pack '{logicalId}' song folder '{name}' skipped: {why}");
                    continue;
                }

                why = validateCharts(info);
                if (why != null)
                {
                    Diagnostics.Warn($"pack '{logicalId}' song folder '{name}' skipped: {why}");
                    continue;
                }

                Diagnostics.Info($"pack '{logicalId}' song '{info.Name}' by {info.Composer}");
                discovered.Charts.Add(info);
            }

            if (discovered.Charts.Count == 0)
            {
                reason = "pack contains no valid songs";
                return false;
            }

            pack = discovered;
            return true;
        }
    }
}
