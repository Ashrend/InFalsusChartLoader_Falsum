using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace InFalsusChartLoader_falsum
{
    internal static class FalsumReader
    {
        private const int Difficulties = 4;

        internal static bool TryRead(string folder, out ChartInfo info, out string reason)
        {
            info = null;
            reason = null;

            string[] packages;
            try
            {
                packages = Directory.GetFiles(folder, "*.falsum");
            }
            catch (Exception e)
            {
                reason = "falsum folder could not be read: " + Diagnostics.Describe(e);
                return false;
            }

            if (packages.Length == 0)
            {
                reason = "no if file and no .falsum package";
                return false;
            }

            string lastReason = null;
            foreach (string package in packages)
            {
                if (TryReadPackage(folder, package, out info, out reason))
                    return true;
                lastReason = reason;
            }

            reason = lastReason;
            return false;
        }

        /// <summary>Whether <paramref name="folder"/> carries at least one <c>.falsum</c> package.</summary>
        internal static bool HasPackage(string folder)
        {
            try
            {
                return Directory.GetFiles(folder, "*.falsum").Length > 0;
            }
            catch (Exception e)
            {
                Diagnostics.Warn("falsum folder could not be read: " + Diagnostics.Describe(e));
                return false;
            }
        }

        private static bool TryReadPackage(string folder, string package, out ChartInfo info, out string reason)
        {
            info = null;
            reason = null;

            try
            {
                using ZipArchive archive = ZipFile.OpenRead(package);
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal)) continue;
                    string name = entry.FullName.Replace('\\', '/');
                    if (IsSafeRelative(name)) entries[name] = entry;
                }

                if (!entries.TryGetValue("song.json", out ZipArchiveEntry songEntry))
                {
                    reason = "falsum has no song.json";
                    return false;
                }

                string json = ReadText(songEntry);
                using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "falsum song.json is not an object";
                    return false;
                }

                string songId = RequiredString(root, "songId");
                if (!SafeSongId(songId))
                {
                    reason = $"unsafe songId: {songId}";
                    return false;
                }

                string title = OptionalString(root, "title") ?? songId;
                string artist = OptionalString(root, "artist") ?? "";
                string jacketDesigner = OptionalString(root, "jacketDesigner") ?? "";
                string background = OptionalString(root, "background");

                if (!root.TryGetProperty("audio", out JsonElement audio) || audio.ValueKind != JsonValueKind.Object)
                {
                    reason = "falsum has no audio section";
                    return false;
                }
                string audioFile = RequiredString(audio, "file");
                double durationMs = AudioDuration(audio);

                string coverFile = null;
                if (root.TryGetProperty("cover", out JsonElement cover) && cover.ValueKind == JsonValueKind.Object)
                    coverFile = OptionalString(cover, "file");

                if (!entries.TryGetValue(LocalPath(audioFile), out ZipArchiveEntry audioEntry))
                {
                    reason = $"falsum audio not found: {audioFile}";
                    return false;
                }

                string cache = CacheDirectory(package);
                string audioPath = Extract(audioEntry, cache, "audio", audioFile);
                string coverPath = null;
                if (!string.IsNullOrEmpty(coverFile) && entries.TryGetValue(LocalPath(coverFile), out ZipArchiveEntry coverEntry))
                    coverPath = Extract(coverEntry, cache, "cover", coverFile);
                else if (!string.IsNullOrEmpty(coverFile))
                    Diagnostics.Warn($"falsum '{Path.GetFileName(package)}': cover is missing: {coverFile}");

                string backgroundPath = null;
                if (!string.IsNullOrEmpty(background) && entries.TryGetValue(LocalPath(background), out ZipArchiveEntry backgroundEntry))
                    backgroundPath = Extract(backgroundEntry, cache, "background", background);
                else if (!string.IsNullOrEmpty(background))
                    Diagnostics.Warn($"falsum '{Path.GetFileName(package)}': background is missing: {background}");

                if (!root.TryGetProperty("difficulties", out JsonElement difficulties) || difficulties.ValueKind != JsonValueKind.Array)
                {
                    reason = "falsum has no difficulties array";
                    return false;
                }

                var chartBytes = new byte[Difficulties][];
                var chartPaths = new string[Difficulties];
                var chartNames = new string[Difficulties];
                var noChart = new bool[Difficulties];
                var charters = new string[Difficulties];
                var levels = new int?[Difficulties];
                var seen = new bool[Difficulties];
                bool any = false;

                foreach (JsonElement item in difficulties.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        reason = "falsum difficulty entry is not an object";
                        return false;
                    }

                    int index = RequiredInt(item, "index");
                    if (index < 0 || index >= Difficulties)
                    {
                        reason = $"falsum difficulty index out of range: {index}";
                        return false;
                    }
                    if (seen[index])
                    {
                        reason = $"falsum duplicate difficulty index: {index}";
                        return false;
                    }
                    seen[index] = true;
                    chartPaths[index] = Path.Combine(folder, songId + index + ".spc");
                    chartNames[index] = songId + index + ".spc";

                    bool available = item.TryGetProperty("available", out JsonElement flag) &&
                                     flag.ValueKind == JsonValueKind.True;
                    if (!available)
                    {
                        noChart[index] = true;
                        continue;
                    }

                    string chartPath = RequiredString(item, "chart");
                    if (!entries.TryGetValue(LocalPath(chartPath), out ZipArchiveEntry chartEntry))
                    {
                        reason = $"falsum chart not found: {chartPath}";
                        return false;
                    }

                    string chartText = ReadText(chartEntry);
                    chartBytes[index] = FalsumChartCompiler.Compile(chartText, chartNames[index]);
                    noChart[index] = false;
                    charters[index] = OptionalString(item, "chartDesigner") ?? "";
                    levels[index] = OptionalInt(item, "rating");
                    any = true;
                }

                if (!any)
                {
                    reason = "falsum has no available difficulty";
                    return false;
                }

                var jacketPaths = new string[Difficulties];
                if (coverPath != null)
                    for (int i = 0; i < Difficulties; i++) jacketPaths[i] = coverPath;

                var backgroundPaths = new string[Difficulties];
                if (backgroundPath != null)
                    for (int i = 0; i < Difficulties; i++) backgroundPaths[i] = backgroundPath;

                string[] illustrators = null;
                if (!string.IsNullOrEmpty(jacketDesigner))
                {
                    illustrators = new string[Difficulties];
                    for (int i = 0; i < Difficulties; i++) illustrators[i] = jacketDesigner;
                }

                info = new ChartInfo
                {
                    Folder = folder,
                    FolderName = Path.GetFileName(folder),
                    BaseName = songId,
                    Name = title,
                    Composer = artist,
                    Illustrators = illustrators,
                    SongPath = audioPath,
                    JacketPaths = jacketPaths,
                    BackgroundPaths = backgroundPath != null ? backgroundPaths : null,
                    PreviewStart = 0f,
                    PreviewEnd = (float)Math.Min(60.0, durationMs / 1000.0),
                    ChartPaths = chartPaths,
                    ChartNames = chartNames,
                    ChartBytes = chartBytes,
                    NoChart = noChart,
                    Charters = charters,
                    Level = levels,
                };
                return true;
            }
            catch (Exception e)
            {
                reason = "falsum could not be read: " + Diagnostics.Describe(e);
                return false;
            }
        }

        private static string ReadText(ZipArchiveEntry entry)
        {
            using StreamReader reader = new StreamReader(entry.Open(), Encoding.UTF8, true);
            return reader.ReadToEnd();
        }

        private static string Extract(ZipArchiveEntry entry, string cache, string role, string original)
        {
            string name = Path.GetFileName(original);
            if (string.IsNullOrEmpty(name)) throw new InvalidDataException("falsum asset has no file name");
            string folder = Path.Combine(cache, role);
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, name);
            using Stream input = entry.Open();
            using FileStream output = File.Create(target);
            input.CopyTo(output);
            return target;
        }

        private static string CacheDirectory(string package)
        {
            var info = new FileInfo(package);
            string key = package + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks;
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            string directory = Path.Combine(Path.GetTempPath(), "InFalsusChartLoader_falsum", "falsum",
                                            Convert.ToHexString(hash).Substring(0, 16));
            Directory.CreateDirectory(directory);
            return directory;
        }

        private static bool SafeSongId(string songId)
        {
            return !string.IsNullOrEmpty(songId) &&
                   songId != "." && songId != ".." &&
                   songId.IndexOf('/') < 0 && songId.IndexOf('\\') < 0 &&
                   songId.IndexOf('\0') < 0;
        }

        private static bool IsSafeRelative(string path)
        {
            if (string.IsNullOrEmpty(path) || path.IndexOf('\\') >= 0 || path.IndexOf('\0') >= 0 ||
                path.StartsWith("/", StringComparison.Ordinal))
                return false;
            string[] parts = path.Split('/');
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == "..") return false;
            return true;
        }

        private static string LocalPath(string path) => path.Replace('\\', '/');

        private static double AudioDuration(JsonElement audio)
        {
            if (audio.TryGetProperty("durationMs", out JsonElement value) &&
                value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double ms))
                return Math.Max(0.0, ms);
            return 60_000.0;
        }

        private static string RequiredString(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.String)
                throw new InvalidDataException($"song.json has no {key}");
            string text = value.GetString();
            if (string.IsNullOrEmpty(text)) throw new InvalidDataException($"song.json has an empty {key}");
            return text;
        }

        private static string OptionalString(JsonElement root, string key)
        {
            return root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        private static int RequiredInt(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.Number ||
                !value.TryGetInt32(out int number))
                throw new InvalidDataException($"song.json has no {key}");
            return number;
        }

        private static int? OptionalInt(JsonElement root, string key)
        {
            if (root.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int number))
                return number;
            return null;
        }
    }
}

