using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace InFalsusChartLoader_falsum
{
    // The `if` key rules here mirror ChartInfoReader.cs, which reads a complete `if` chart folder.
    // Change both files together whenever the `if` schema changes.
    /// <summary>The display overrides a song folder's <c>if</c> gives when a <c>.falsum</c> is the base.</summary>
    /// <remarks>
    /// Every field is optional: <c>null</c> means the <c>if</c> did not give it. Only display facts are
    /// read here; chart names, audio and ids stay with the <c>.falsum</c> package.
    /// </remarks>
    internal sealed class IfOverlay
    {
        /// <summary>The file that carries the overlay. Lower case, and matched as written.</summary>
        internal const string InfoFileName = "if";

        /// <summary>The image formats a jacket may be in.</summary>
        private static readonly string[] JacketExtensions = { ".png" };

        /// <summary>The in-play background formats.</summary>
        private static readonly string[] BackgroundExtensions = { ".png", ".mp4" };

        /// <summary>How the file is read.</summary>
        private static readonly JsonDocumentOptions JsonOptions = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        /// <summary>Display name of the song, or null when the <c>if</c> does not give one.</summary>
        internal string Name;

        /// <summary>Composer, or null when the <c>if</c> does not give one.</summary>
        internal string Composer;

        /// <summary>Whether the <c>if</c> gives a song-select preview window.</summary>
        internal bool HasPreview;

        /// <summary>The start of the preview, in seconds.</summary>
        internal float PreviewStart;

        /// <summary>The end of the preview, in seconds.</summary>
        internal float PreviewEnd;

        /// <summary>Illustrators, one per difficulty, or null when the <c>if</c> does not give them.</summary>
        internal string[] Illustrators;

        /// <summary>Absolute paths of the four jacket images, or null when the <c>if</c> does not give them.</summary>
        internal string[] Jackets;

        /// <summary>Absolute paths of the four backgrounds, or null when the <c>if</c> does not give them.</summary>
        internal string[] Backgrounds;

        /// <summary>Chart designers, one per difficulty, or null when the <c>if</c> does not give them.</summary>
        internal string[] Charters;

        /// <summary>Difficulty ratings, one per difficulty, or null when the <c>if</c> does not give them.</summary>
        internal int?[] Levels;

        /// <summary>Whether the <c>if</c> gives at least one display field.</summary>
        internal bool Any =>
            Name != null || Composer != null || HasPreview ||
            Illustrators != null || Jackets != null || Backgrounds != null ||
            Charters != null || Levels != null;

        /// <summary>Reads <paramref name="folder"/>'s <c>if</c> as a display overlay.</summary>
        internal static bool TryRead(string folder, out IfOverlay overlay, out string reason)
        {
            overlay = null;
            reason = null;

            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath))
            {
#if DEBUG
                reason = $"{InfoFileName} is missing";
#endif
                return false;
            }

            string text;
            try
            {
                text = File.ReadAllText(ifPath);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} could not be read: {e.Message}";
#else
                _ = e;
#endif
                return false;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text, JsonOptions);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} is not valid JSON: {e.Message}";
#else
                _ = e;
#endif
                return false;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
#if DEBUG
                    reason = $"{InfoFileName} is not a JSON object";
#endif
                    return false;
                }

                var built = new IfOverlay();
                if (!ReadText(root, "name", out built.Name, out reason)) return false;
                if (!ReadText(root, "composer", out built.Composer, out reason)) return false;
                if (!ReadPreview(root, out built.HasPreview, out built.PreviewStart, out built.PreviewEnd,
                                 out reason)) return false;
                if (!ReadIllustrators(root, out built.Illustrators, out reason)) return false;
                if (!ReadOneOrFour(root, folder, "jacket", JacketExtensions, false, out built.Jackets,
                                   out reason)) return false;
                if (!ReadOneOrFour(root, folder, "background", BackgroundExtensions, true, out built.Backgrounds,
                                   out reason)) return false;
                if (!ReadCharters(root, out built.Charters, out reason)) return false;
                if (!ReadLevels(root, out built.Levels, out reason)) return false;

                overlay = built;
                return true;
            }
        }

        /// <summary>Applies the display fields this overlay gives, leaving the rest of <paramref name="info"/> alone.</summary>
        internal void Apply(ChartInfo info)
        {
            if (Name != null) info.Name = Name;
            if (Composer != null) info.Composer = Composer;

            if (HasPreview)
            {
                info.PreviewStart = PreviewStart;
                info.PreviewEnd = PreviewEnd;
            }

            if (Illustrators != null) info.Illustrators = Illustrators;
            if (Jackets != null) info.JacketPaths = Jackets;
            if (Backgrounds != null) info.BackgroundPaths = Backgrounds;

            if (Charters != null)
            {
                if (info.Charters == null) info.Charters = new string[ChartInfo.Difficulties];
                for (int d = 0; d < ChartInfo.Difficulties; d++)
                {
                    if (info.NoChart[d] || !string.IsNullOrEmpty(info.Charters[d])) continue;
                    if (!string.IsNullOrEmpty(Charters[d])) info.Charters[d] = Charters[d];
                }
            }

            if (Levels != null)
            {
                if (info.Level == null) info.Level = new int?[ChartInfo.Difficulties];
                for (int d = 0; d < ChartInfo.Difficulties; d++)
                {
                    if (info.NoChart[d] || info.Level[d] != null) continue;
                    if (Levels[d].HasValue) info.Level[d] = Levels[d];
                }
            }
        }

        /// <summary>An optional, non-empty string field.</summary>
        private static bool ReadText(JsonElement root, string key, out string value, out string reason)
        {
            value = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            if (token.ValueKind != JsonValueKind.String)
            {
#if DEBUG
                reason = $"\"{key}\" is not a string";
#endif
                return false;
            }

            value = token.GetString();
            if (string.IsNullOrWhiteSpace(value))
            {
#if DEBUG
                reason = $"\"{key}\" is empty";
#endif
                value = null;
                return false;
            }

            return true;
        }

        /// <summary>An optional key that is one file name or four.</summary>
        private static bool ReadOneOrFour(JsonElement root, string folder, string key, string[] allowed,
                                          bool entriesMayBeNull, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            if (!root.TryGetProperty(key, out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            if (token.ValueKind == JsonValueKind.String)
            {
                if (!ReadLocalFile(folder, key, token.GetString(), allowed, out string single, out reason))
                    return false;

                var same = new string[ChartInfo.Difficulties];
                for (int i = 0; i < same.Length; i++) same[i] = single;
                paths = same;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = $"\"{key}\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null && entriesMayBeNull) continue;   // this one: none

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }

                if (!ReadLocalFile(folder, key, item.GetString(), allowed, out result[i], out reason))
                    return false;
            }

            paths = result;
            return true;
        }

        /// <summary>The illustrators: one name or four.</summary>
        private static bool ReadIllustrators(JsonElement root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            if (!root.TryGetProperty("illust", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            if (token.ValueKind == JsonValueKind.String)
            {
                string single = token.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
#if DEBUG
                    reason = "\"illust\" is empty";
#endif
                    return false;
                }

                names = new string[ChartInfo.Difficulties];
                for (int i = 0; i < names.Length; i++) names[i] = single;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"illust\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"illust\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null)
                {
                    result[i] = string.Empty;
                    continue;
                }

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"illust\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            names = result;
            return true;
        }

        /// <summary>The preview window, in seconds: two numbers or numeric strings.</summary>
        private static bool ReadPreview(JsonElement root, out bool hasPreview, out float start, out float end,
                                        out string reason)
        {
            hasPreview = false;
            start = 0f;
            end = 0f;
            reason = null;

            if (!root.TryGetProperty("preview_seconds", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"preview_seconds\" is not a list";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != 2)
            {
#if DEBUG
                reason = $"\"preview_seconds\" has {count} entries, not 2";
#endif
                return false;
            }

            var values = new float[2];
            for (int i = 0; i < values.Length; i++)
            {
                JsonElement item = token[i];
                bool ok;
                if (item.ValueKind == JsonValueKind.Number && item.TryGetDouble(out double number))
                {
                    values[i] = (float)number;
                    ok = true;
                }
                else
                {
                    ok = item.ValueKind == JsonValueKind.String &&
                         float.TryParse(item.GetString(), NumberStyles.Float,
                                        CultureInfo.InvariantCulture, out values[i]);
                }

                if (!ok)
                {
#if DEBUG
                    reason = $"\"preview_seconds\"[{i}] is not a number of seconds";
#endif
                    return false;
                }
            }

            start = values[0];
            end = values[1];
            hasPreview = true;
            return true;
        }

        /// <summary>The chart designers: one name or four.</summary>
        private static bool ReadCharters(JsonElement root, out string[] charters, out string reason)
        {
            charters = null;
            reason = null;

            if (!root.TryGetProperty("charter", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            var result = new string[ChartInfo.Difficulties];
            if (token.ValueKind == JsonValueKind.String)
            {
                string single = token.GetString();
                if (string.IsNullOrWhiteSpace(single))
                {
#if DEBUG
                    reason = "\"charter\" is empty";
#endif
                    return false;
                }

                result[result.Length - 1] = single;   // one value belongs to the last difficulty
                charters = result;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"charter\" is neither a name nor a list of names";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"charter\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                {
#if DEBUG
                    reason = $"\"charter\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.GetString();
            }

            charters = result;
            return true;
        }

        /// <summary>The difficulty ratings: one positive whole number or four.</summary>
        private static bool ReadLevels(JsonElement root, out int?[] levels, out string reason)
        {
            levels = null;
            reason = null;

            if (!root.TryGetProperty("lv", out JsonElement token) ||
                token.ValueKind == JsonValueKind.Null) return true;   // absent: the falsum's own

            static bool Whole(JsonElement item, out int value)
            {
                value = 0;
                if (item.ValueKind != JsonValueKind.Number) return false;

                string raw = item.GetRawText();
                if (raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0) return false;

                return item.TryGetInt32(out value);
            }

            var result = new int?[ChartInfo.Difficulties];
            if (token.ValueKind == JsonValueKind.Number)
            {
                if (!Whole(token, out int single) || single <= 0)
                {
#if DEBUG
                    reason = "\"lv\" is not a positive whole number";
#endif
                    return false;
                }

                result[result.Length - 1] = single;   // one value belongs to the last difficulty
                levels = result;
                return true;
            }

            if (token.ValueKind != JsonValueKind.Array)
            {
#if DEBUG
                reason = "\"lv\" is neither a number nor a list of numbers";
#endif
                return false;
            }

            int count = token.GetArrayLength();
            if (count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"lv\" has {count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            for (int i = 0; i < result.Length; i++)
            {
                JsonElement item = token[i];
                if (item.ValueKind == JsonValueKind.Null) continue;

                if (!Whole(item, out int value) || value <= 0)
                {
#if DEBUG
                    reason = $"\"lv\"[{i}] is not a positive whole number";
#endif
                    return false;
                }
                result[i] = value;
            }

            levels = result;
            return true;
        }

        /// <summary>One local file name, checked against <paramref name="allowed"/> and the folder.</summary>
        private static bool ReadLocalFile(string folder, string key, string name, string[] allowed,
                                          out string path, out string reason)
        {
            path = null;
            reason = null;

            if (string.IsNullOrWhiteSpace(name) || Path.GetFileName(name) != name)
            {
#if DEBUG
                reason = $"\"{key}\" names something outside the folder: {name}";
#endif
                return false;
            }

            string extension = Path.GetExtension(name).ToLowerInvariant();
            if (Array.IndexOf(allowed, extension) < 0)
            {
#if DEBUG
                reason = $"\"{key}\" is {extension}, which the game cannot decode " +
                         $"(accepted: {string.Join(", ", allowed)})";
#endif
                return false;
            }

            path = Path.Combine(folder, name);
            if (!File.Exists(path))
            {
#if DEBUG
                reason = $"\"{key}\" points at {name}, which is not in the folder";
#endif
                path = null;
                return false;
            }

            return true;
        }
    }
}
