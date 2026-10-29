using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The <c>Charts/</c> folder: where it is, and what it contains.</summary>
    internal static class ChartLibrary
    {
        /// <summary>The folder name to look for, and the name in the "beside the mod folder" rule.</summary>
        private const string FolderName = "Charts";

        /// <summary>The folder the DLL was loaded from.</summary>
        internal static string ModFolder()
        {
            try
            {
                string location = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(location)) return Path.GetDirectoryName(location);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"could not read this assembly's path: {Diagnostics.Describe(e)}");
            }
            return null;
        }

        /// <summary>Where the charts are, creating the folder if it is not there.</summary>
        internal static string Locate()
        {
            string mod = ModFolder();
            if (mod == null) return null;

            DirectoryInfo parent = Directory.GetParent(mod);
            string[] candidates =
            {
#if BEPINEX
                parent?.Parent == null
                    ? null
                    : Path.GetFullPath(Path.Combine(parent.Parent.FullName, FolderName)),
#endif
                parent == null ? null : Path.GetFullPath(Path.Combine(parent.FullName, FolderName)),
                Path.GetFullPath(Path.Combine(mod, FolderName)),
            };

            foreach (string candidate in candidates)
            {
                if (candidate == null) continue;
                if (Directory.Exists(candidate))
                {
                    Diagnostics.Info($"{FolderName} folder: {candidate}");
                    return candidate;
                }
            }

            string preferred = candidates[0] ?? candidates[1];
            if (preferred == null)
            {
                Diagnostics.Warn("there is nowhere to put a charts folder");
                return null;
            }

            try
            {
                Directory.CreateDirectory(preferred);
                Diagnostics.Info($"created the {FolderName} folder at {preferred}");
                Diagnostics.Info("drop one folder per song in there, each holding an `if` and its files");
                return preferred;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"no {FolderName} folder, and one could not be created at {preferred}: " +
                                 Diagnostics.Describe(e));
                return preferred;
            }
        }

        /// <summary>The display name of the pack used by legacy root-level songs.</summary>
        private const string LegacyPackName = "IFCL";

        /// <summary>Imports every complete chart folder under <paramref name="root"/>.</summary>
        internal static List<ChartPack> Import(string root, Func<ChartInfo, string> validateCharts)
        {
            var imported = new List<ChartPack>();
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                Diagnostics.Warn($"no {FolderName} folder at {root}; nothing to load");
                return imported;
            }

            string[] folders;
            try
            {
                folders = Directory.GetDirectories(root);
                Array.Sort(folders, StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{root} could not be listed: {Diagnostics.Describe(e)}");
                return imported;
            }

            ChartPack legacy = null;

            foreach (string folder in folders)
            {
                string name = Path.GetFileName(folder);

                if (name.StartsWith("[Pack]", StringComparison.Ordinal))
                {
                    if (!PackManifest.TryGetFolderId(name, out string id, out string idReason))
                    {
                        Diagnostics.Warn($"pack folder '{name}' skipped: {idReason}");
                        continue;
                    }

                    if (!PackReader.TryRead(folder, id, validateCharts, out ChartPack pack, out string reason))
                    {
                        Diagnostics.Warn($"pack folder '{name}' skipped: {reason}");
                        continue;
                    }

                    Diagnostics.Info($"pack folder '{name}': {pack.Charts.Count} song(s)");
                    imported.Add(pack);
                    continue;
                }

                if (!ChartInfoReader.TryRead(folder, out ChartInfo info, out string chartReason))
                {
                    Diagnostics.Warn($"chart folder '{name}' skipped: {chartReason}");
                    continue;
                }

                string validation = validateCharts(info);
                if (validation != null)
                {
                    Diagnostics.Warn($"chart folder '{name}' skipped: {validation}");
                    continue;
                }

                if (legacy == null)
                {
                    legacy = new ChartPack
                    {
                        Folder = root,
                        LogicalId = LegacyPackName,
                        Names = LegacyNames(),
                        IsLegacy = true,
                    };
                }

                Diagnostics.Info($"chart folder '{name}': '{info.Name}' by {info.Composer}, " +
                                 $"{Levels(info)}");
                legacy.Charts.Add(info);
            }

            if (legacy != null) imported.Add(legacy);
            return imported;
        }

        /// <summary>The legacy pack's name in every language slot.</summary>
        private static string[] LegacyNames()
        {
            var names = new string[PackManifest.Languages];
            for (int i = 0; i < names.Length; i++) names[i] = LegacyPackName;
            return names;
        }

        /// <summary>The four ratings for a log line, with <c>-</c> for a difficulty left out.</summary>
        private static string Levels(ChartInfo info)
        {
            var text = new string[ChartInfo.Difficulties];
            for (int d = 0; d < text.Length; d++) text[d] = info.Level[d]?.ToString() ?? "-";
            return string.Join("/", text);
        }
    }
}
