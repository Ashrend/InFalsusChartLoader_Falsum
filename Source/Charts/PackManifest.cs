using System;
using System.Text.Json;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The two columns the pack select screen files a pack under.</summary>
    internal enum PackCategory
    {
        Story,
        Song,
    }

    /// <summary>The `pack` document and the language slots it fills.</summary>
    internal sealed class PackManifest
    {
        /// <summary>The five language slots in `TextMappingValues` order.</summary>
        internal const int Languages = 5;

        /// <summary>The JSON keys, in the same order as the game's language slots.</summary>
        private static readonly string[] LanguageKeys = { "en", "ja", "ko", "zh-Hant", "zh-Hans" };

        /// <summary>The fallback order used when a language is absent.</summary>
        private static readonly int[] FallbackOrder = { 0, 1, 2, 3, 4 };

        /// <summary>The accepted spellings of the category key.</summary>
        private static readonly string[] CategoryKeys = { "pack", "category", "type" };

        /// <summary>The pack's display name in every language slot.</summary>
        internal string[] Names;

        /// <summary>Which pack-select column this pack belongs to.</summary>
        internal PackCategory Category = PackCategory.Story;

        /// <summary>Reads the folder suffix of a `[Pack]` directory.</summary>
        internal static bool TryGetFolderId(string folderName, out string id, out string reason)
        {
            id = null;
            reason = null;

            const string Prefix = "[Pack]";
            if (string.IsNullOrEmpty(folderName) ||
                !folderName.StartsWith(Prefix, StringComparison.Ordinal))
            {
                reason = "folder does not start with [Pack]";
                return false;
            }

            string suffix = folderName.Substring(Prefix.Length);
            if (string.IsNullOrWhiteSpace(suffix) ||
                suffix.IndexOf('/') >= 0 || suffix.IndexOf('\\') >= 0 ||
                suffix.IndexOf('\0') >= 0)
            {
                reason = "the [Pack] folder has no usable id";
                return false;
            }

            id = suffix;
            return true;
        }

        /// <summary>Reads a `pack` JSON document.</summary>
        internal static bool TryParse(string json, out PackManifest manifest, out string reason)
        {
            manifest = null;
            reason = null;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                });
            }
            catch (Exception e)
            {
                reason = "pack is not valid JSON: " + e.Message;
                return false;
            }

            using (document)
            {
                JsonElement root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    reason = "pack is not a JSON object";
                    return false;
                }

                if (!root.TryGetProperty("name", out JsonElement names) ||
                    names.ValueKind != JsonValueKind.Object)
                {
                    reason = "pack has no name object";
                    return false;
                }

                var values = new string[Languages];
                bool any = false;

                foreach (JsonProperty property in names.EnumerateObject())
                {
                    int index = Array.IndexOf(LanguageKeys, property.Name);
                    if (index < 0) continue;   // unknown fields are left for later versions
                    if (property.Value.ValueKind != JsonValueKind.String) continue;

                    string text = property.Value.GetString();
                    if (string.IsNullOrWhiteSpace(text)) continue;

                    values[index] = text;
                    any = true;
                }

                if (!any)
                {
                    reason = "pack has no non-empty name";
                    return false;
                }

                string fallback = null;
                foreach (int index in FallbackOrder)
                {
                    if (values[index] == null) continue;
                    fallback = values[index];
                    break;
                }

                for (int i = 0; i < values.Length; i++)
                    if (values[i] == null) values[i] = fallback;

                PackCategory category = PackCategory.Story;
                foreach (string key in CategoryKeys)
                {
                    if (!root.TryGetProperty(key, out JsonElement value)) continue;
                    if (value.ValueKind == JsonValueKind.Null) break;
                    if (value.ValueKind != JsonValueKind.String)
                    {
                        reason = "pack has an invalid category";
                        return false;
                    }

                    string text = value.GetString()?.Trim();
                    if (string.Equals(text, "story", StringComparison.OrdinalIgnoreCase))
                        category = PackCategory.Story;
                    else if (string.Equals(text, "song", StringComparison.OrdinalIgnoreCase))
                        category = PackCategory.Song;
                    else
                    {
                        reason = "pack has an unknown category: " + text;
                        return false;
                    }
                    break;
                }

                manifest = new PackManifest { Names = values, Category = category };
                return true;
            }
        }
    }
}
