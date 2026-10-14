using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;

namespace InFalsusChartLoader
{
    /// <summary>
    /// Reads and vets one chart folder's <c>if</c> file.
    ///
    /// <para>
    /// Everything here is a rejection rule. A folder either produces a complete <see cref="ChartInfo"/>
    /// or it produces nothing at all plus a reason — there is no half-imported song, because a song
    /// missing one of its four difficulties has nowhere to be shown and a song whose audio is absent
    /// cannot be played.
    /// </para>
    /// <para>
    /// The rules come from <c>Charts.EXAMPLE/READ</c>: the three arrays must be exactly four long,
    /// and the audio and image formats are restricted to what the game itself can decode. On top of
    /// those, a referenced file that is not there is a rejection — a name in the file is a claim
    /// about the disk, and the disk is what decides.
    /// </para>
    /// <para>
    /// Two fields are newer than that document and are described in <c>IF_FORMAT_V2.md</c>:
    /// <c>id</c>, which is the name the song is filed under, and <c>preview_seconds</c>, the window
    /// the song-select previews. Both are <b>required</b>, like every other field. They used to be
    /// optional, defaulting to the folder's name and to the game's own 20-to-30-second window; that
    /// is gone. A song silently filed under a name its author did not choose, previewing a window
    /// its author did not set, is a defect the author cannot see — and neither default was ever a
    /// decision anybody made, only one nobody noticed. Leaving either out is a rejection, and the
    /// reason names the field.
    /// </para>
    /// <para>
    /// The v3 format is described in <c>IF_FORMAT_V3.md</c> and makes four changes here: the jacket
    /// key is <c>jacket</c> and the chart designers' key is <c>charter</c>; <c>illust</c> and
    /// <c>background</c> are <b>optional</b> — the two fields whose absence is an answer rather than
    /// a rejection, because neither is a claim about a file that has to exist for the song to be
    /// playable: the game draws its own background and hides its own illustrator element, and in both
    /// cases "the author wrote nothing" is what makes that happen. Both take one name or four, and
    /// <c>background</c> takes a still or a clip.
    /// </para>
    /// <para>
    /// A single string still counts as four copies of itself wherever one name or four is accepted —
    /// the jacket's older form, kept because it is what most folders want.
    /// </para>
    /// <para>
    /// Every reason assignment is inside <c>#if DEBUG</c>. That looks like clutter and is not: the
    /// only consumer is a <c>[Conditional("DEBUG")]</c> log call, so in a Release build these
    /// messages can never be printed — and a string built in a normal method ships whether or not
    /// anything reads it. Leaving them unguarded costs the release about a kilobyte of text that
    /// does nothing, which is the exact thing build.bat's artifact check exists to catch. The
    /// <c>#if</c> cannot be moved onto a helper either: a conditional method may not have an
    /// <c>out</c> parameter, and every one of these sets one.
    /// </para>
    /// </summary>
    internal static class ChartInfoReader
    {
        /// <summary>
        /// Audio containers this mod will hand to the game.
        ///
        /// The game's own audio is Ogg Vorbis — its <c>.wav</c> files are Ogg streams under a
        /// mislabelled extension — and FMOD, which is what plays it, also has MP3 and RIFF/WAVE
        /// compiled in.
        /// </summary>
        private static readonly string[] AudioExtensions = { ".wav", ".mp3", ".ogg" };

        /// <summary>
        /// Image formats a jacket may be in — PNG only.
        ///
        /// Not a preference. The game reaches every image through Addressables and has no
        /// decode-from-bytes entry point in its metadata, so this mod has to decode the file itself,
        /// and a PNG decoder is one this mod can carry while a JPEG decoder is not. `READ` makes the
        /// format conditional on exactly this ("若并不支持 jpg 则合法图片格式为 png"), so PNG-only is
        /// the branch that applies. Accepting `.jpg` here would be accepting a file nothing can draw.
        /// </summary>
        private static readonly string[] ImageExtensions = { ".png" };

        /// <summary>
        /// What an in-play background may be: a still, or a clip.
        ///
        /// The still goes through the same decoder a jacket does. The clip is played by the game's
        /// own video module — `UnityEngine.Video.VideoPlayer`, present in this build's metadata and
        /// already used by the game itself (`C2DVideoPlayer`, `GenericMeshVideoPlayer`) — into a
        /// render texture the background material shows. MP4 is the container Unity's Windows player
        /// decodes through the OS codecs; whether a given file plays is the platform's answer, which
        /// is why the player's state is reported in the probe rather than assumed.
        /// </summary>
        private static readonly string[] BackgroundExtensions = { ".png", ".mp4" };

        /// <summary>The file that carries a folder's metadata. Lower case, and matched as written.</summary>
        internal const string InfoFileName = "if";

        /// <summary>
        /// Reads <paramref name="folder"/>. On success <paramref name="info"/> is complete and every
        /// path in it exists. On failure it is null and <paramref name="reason"/> says which rule
        /// broke — in a Debug build, where the value is actually filled in.
        /// </summary>
        internal static bool TryRead(string folder, out ChartInfo info, out string reason)
        {
            info = null;
            reason = null;

            string ifPath = Path.Combine(folder, InfoFileName);
            if (!File.Exists(ifPath))
            {
#if DEBUG
                reason = $"no {InfoFileName} file";
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
                reason = $"{InfoFileName} could not be read: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(text);
            }
            catch (Exception e)
            {
#if DEBUG
                reason = $"{InfoFileName} is not valid JSON: {Diagnostics.Describe(e)}";
#else
                _ = e;
#endif
                return false;
            }

            string folderName = Path.GetFileName(folder);
            var built = new ChartInfo { Folder = folder, FolderName = folderName };

            // `id` names the chart: the game files the song under it and builds `{id}{difficulty}.spc`
            // from it, so it is the string everything downstream answers to. Required, and
            // deliberately not defaulted to the folder's name — that default made a missing `id` look
            // like a working song under a name its author never chose.
            if (!Text(root, "id", out string id, out reason)) return false;
            built.BaseName = id;

            if (!Text(root, "name", out built.Name, out reason)) return false;
            if (!Text(root, "composer", out built.Composer, out reason)) return false;

            // Optional since v3: an author who names no illustrator gets the game's own empty-value
            // path, where the element that would show one is hidden. See `Illustrators` for why that
            // and not "leave the table alone".
            if (!Illustrators(root, out built.Illustrators, out reason)) return false;

            // `song` names a file in this folder and it must be there: an `if` that points at nothing
            // describes a song that cannot be played.
            if (!LocalFile(root, folder, "song", AudioExtensions, out built.SongPath, out reason)) return false;

            if (!Jackets(root, folder, out built.JacketPaths, out reason)) return false;

            // Optional, and absent means "the game's own background": the parameter is the only thing
            // that ever replaces it, so an author who writes nothing keeps whatever the game draws.
            if (!Backgrounds(root, folder, out built.BackgroundPaths, out reason)) return false;

            if (!Preview(root, out built.PreviewStart, out built.PreviewEnd, out reason)) return false;

            // `chart`, `charter` and `lv` are one family and they line up slot for slot. A single
            // value is the last difficulty's — `[null, null, null, value]` — so which of the two ways
            // an author wrote one says nothing about what it means. See `Slots` and `Aligned`.
            if (!DifficultyValue(root, "chart", out JToken charts, out reason)) return false;
            if (!DifficultyValue(root, "charter", out JToken charters, out reason)) return false;
            if (!DifficultyValue(root, "lv", out JToken levels, out reason)) return false;

            JToken[] chartSlots = Slots(charts);
            JToken[] charterSlots = Slots(charters);
            JToken[] levelSlots = Slots(levels);
            if (!Aligned(chartSlots, charterSlots, levelSlots, out reason)) return false;

            if (!Texts(charterSlots, "charter", out built.Charters, out reason)) return false;
            if (!Levels(levelSlots, "lv", out built.Level, out reason)) return false;
            if (!ChartFiles(chartSlots, folder, out built.ChartPaths, out built.ChartNames, out built.NoChart,
                            out reason)) return false;

            info = built;
            return true;
        }

        /// <summary>A required, non-empty string field.</summary>
        private static bool Text(JObject root, string key, out string value, out string reason)
        {
            value = null;
            reason = null;

            JToken token = root[key];
            if (token == null || token.Type == JTokenType.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.Type != JTokenType.String)
            {
#if DEBUG
                reason = $"\"{key}\" is not a string";
#endif
                return false;
            }

            value = token.Value<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
#if DEBUG
                reason = $"\"{key}\" is empty";
#endif
                return false;
            }

            return true;
        }

        /// <summary>
        /// A string field that names a file in the folder, with an extension from
        /// <paramref name="allowed"/>. The file has to exist, and the name has to stay inside the
        /// folder — a name with a separator or a <c>..</c> in it is refused rather than resolved.
        /// </summary>
        private static bool LocalFile(JObject root, string folder, string key, string[] allowed,
                                      out string path, out string reason)
        {
            path = null;
            if (!Text(root, key, out string name, out reason)) return false;

            return LocalFileAt(folder, key, name, allowed, out path, out reason);
        }

        /// <summary>
        /// The rules for one file name from an `if` file, wherever it appeared.
        ///
        /// Split out from <see cref="LocalFile"/> because the jacket list names four files under one
        /// key, and four copies of a rule that lives in two places is how one of them ends up
        /// checking less than the other.
        /// </summary>
        private static bool LocalFileAt(string folder, string key, string name, string[] allowed,
                                        out string path, out string reason)
        {
            path = null;
            reason = null;

            if (Path.GetFileName(name) != name)
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

        /// <summary>
        /// The jacket images: one file per difficulty, under the key <c>jacket</c>.
        ///
        /// A single string counts as four copies of itself. That is the shape the previous format
        /// had, and it is still what most folders want — one picture for a whole song — so refusing
        /// it would break every `if` file written before the list existed for no gain. Four entries
        /// is the general case, and lets a difficulty look unlike its neighbours.
        /// </summary>
        private static bool Jackets(JObject root, string folder, out string[] paths, out string reason) =>
            OneOrFour(root, folder, "jacket", ImageExtensions, false, out paths, out reason);

        /// <summary>
        /// The in-play backgrounds: one file per difficulty, under the key <c>background</c>, each a
        /// still or a clip. Optional — the only field whose absence is an answer rather than a
        /// rejection, because "no background of my own" is an ordinary song. So is one difficulty's
        /// absence: <c>null</c> in an entry keeps the game's own background for that difficulty while
        /// the others wear theirs.
        /// </summary>
        private static bool Backgrounds(JObject root, string folder, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            JToken token = root["background"];
            if (token == null || token.Type == JTokenType.Null) return true;   // absent: the game's own

            return OneOrFour(root, folder, "background", BackgroundExtensions, true, out paths, out reason);
        }

        /// <summary>
        /// A field that is one name or four names, each naming a file in the folder. Shared by the
        /// jackets and the backgrounds, which differ in three things: their key, what a name may end
        /// with, and whether an entry may be left out — <paramref name="entriesMayBeNull"/>, true for
        /// the backgrounds, where naming no file for one difficulty is the same statement the key's
        /// own absence makes for all four.
        /// </summary>
        private static bool OneOrFour(JObject root, string folder, string key, string[] allowed,
                                      bool entriesMayBeNull, out string[] paths, out string reason)
        {
            paths = null;
            reason = null;

            JToken token = root[key];
            if (token == null || token.Type == JTokenType.Null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (token.Type == JTokenType.String)
            {
                if (!LocalFile(root, folder, key, allowed, out string single, out reason)) return false;

                var same = new string[ChartInfo.Difficulties];
                for (int i = 0; i < same.Length; i++) same[i] = single;
                paths = same;
                return true;
            }

            if (token.Type != JTokenType.Array)
            {
#if DEBUG
                reason = $"\"{key}\" is neither a name nor a list of names";
#endif
                return false;
            }

            var array = (JArray)token;
            if (array.Count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {array.Count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JToken item = array[i];
                if (item.Type == JTokenType.Null && entriesMayBeNull) continue;   // this one: none

                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }

                if (!LocalFileAt(folder, key, item.Value<string>(), allowed, out result[i], out reason))
                    return false;
            }

            paths = result;
            return true;
        }

        /// <summary>
        /// The illustrators: one name, four names, or nothing at all — and any one difficulty may be
        /// left out on its own, by writing `null` in its place.
        ///
        /// Nothing at all is not a rejection — see <see cref="ChartInfo.Illustrators"/> — so the only
        /// ways to fail are the ones a written value can fail in: a wrong type, a wrong length, or an
        /// entry that is neither a name nor null. This is the one key that is not part of the
        /// `chart`/`charter`/`lv` family: an illustrator is optional everywhere, so a null here says
        /// nothing about whether the difficulty has a chart, and a difficulty with a chart may leave
        /// its illustrator out. An entry left out is stored as an empty name, which is what hides the
        /// element; see <see cref="ChartInfo.Illustrators"/> for why not just leaving it unwritten.
        /// </summary>
        private static bool Illustrators(JObject root, out string[] names, out string reason)
        {
            names = null;
            reason = null;

            JToken token = root["illust"];
            if (token == null || token.Type == JTokenType.Null) return true;   // absent: the game's own

            if (token.Type == JTokenType.String)
            {
                string single = token.Value<string>();
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

            if (token.Type != JTokenType.Array)
            {
#if DEBUG
                reason = "\"illust\" is neither a name nor a list of names";
#endif
                return false;
            }

            var array = (JArray)token;
            if (array.Count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"illust\" has {array.Count} entries, not {ChartInfo.Difficulties}";
#endif
                return false;
            }

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JToken item = array[i];
                if (item.Type == JTokenType.Null)
                {
                    result[i] = string.Empty;   // this difficulty shows none
                    continue;
                }

                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
#if DEBUG
                    reason = $"\"illust\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.Value<string>();
            }

            names = result;
            return true;
        }

        /// <summary>
        /// The preview window, <c>[start, end]</c> in seconds.
        ///
        /// Required, like the rest of the file. It used to be optional, with the two values written
        /// as -1 so the game's own window applied (20 to 30 seconds) — which is a different part of
        /// the song from the one the author meant, and neither the game nor the log said so. A window
        /// of zeros is a real window at the very start of a song, so there is no value that could
        /// have stood for "not said".
        /// </summary>
        private static bool Preview(JObject root, out float start, out float end, out string reason)
        {
            start = 0f;
            end = 0f;
            reason = null;

            JToken token = root["preview_seconds"];
            if (token == null || token.Type == JTokenType.Null)
            {
#if DEBUG
                reason = "\"preview_seconds\" is missing";
#endif
                return false;
            }

            if (token.Type != JTokenType.Array)
            {
#if DEBUG
                reason = "\"preview_seconds\" is not a list";
#endif
                return false;
            }

            var array = (JArray)token;
            if (array.Count != 2)
            {
#if DEBUG
                reason = $"\"preview_seconds\" has {array.Count} entries, not 2";
#endif
                return false;
            }

            if (!Seconds(array[0], 0, out start, out reason)) return false;
            if (!Seconds(array[1], 1, out end, out reason)) return false;
            return true;
        }

        /// <summary>
        /// One of the two preview values.
        ///
        /// A number is taken as one, and so is a number written as a string — the field was
        /// specified with its values quoted, so a reader that refused that would be refusing the
        /// format as written. Parsed invariantly: a locale that reads <c>0,5</c> as a half would
        /// otherwise turn a window into something else entirely.
        /// </summary>
        private static bool Seconds(JToken item, int index, out float value, out string reason)
        {
            value = 0f;
            reason = null;

            switch (item.Type)
            {
                case JTokenType.Integer:
                case JTokenType.Float:
                    value = item.Value<float>();
                    return true;

                case JTokenType.String:
                    if (float.TryParse(item.Value<string>(), NumberStyles.Float,
                                       CultureInfo.InvariantCulture, out value)) return true;
                    break;
            }

#if DEBUG
            reason = $"\"preview_seconds\"[{index}] is not a number of seconds";
#endif
            return false;
        }

        /// <summary>
        /// One of the three per-difficulty keys, as its token: a four-element array, or one value.
        ///
        /// Either way it is a statement about <b>four</b> difficulties — see <see cref="Slots"/>, which
        /// turns the one-value form into the four-element one it is short for. All this settles is
        /// that the key is there, and that an array of it is neither short nor long; what the values
        /// say — including a bare `null`, which is the one-value form of "none" — is
        /// <see cref="Aligned"/>'s question and not this one's.
        /// </summary>
        private static bool DifficultyValue(JObject root, string key, out JToken token, out string reason)
        {
            token = null;
            reason = null;

            JToken found = root[key];
            if (found == null)
            {
#if DEBUG
                reason = $"\"{key}\" is missing";
#endif
                return false;
            }

            if (found.Type == JTokenType.Array && ((JArray)found).Count != ChartInfo.Difficulties)
            {
#if DEBUG
                reason = $"\"{key}\" has {((JArray)found).Count} entries, not {ChartInfo.Difficulties} "
                       + "and not one";
#endif
                return false;
            }

            token = found;
            return true;
        }

        /// <summary>
        /// One of those tokens as its four difficulties: the array as written, or a single value
        /// written out as <c>[null, null, null, value]</c>.
        ///
        /// The single value states the <b>last</b> difficulty and no other, and that is the rule for
        /// all three keys — one value in `chart` is one chart, for Forbidden, so one value in
        /// `charter` is that difficulty's designer and one value in `lv` is its rating. It used to be
        /// read two ways, a single `charter` or `lv` meaning every difficulty's; that made the three
        /// keys disagree about what the same spelling says, and it put a designer on difficulties
        /// whose `chart` entry said there was no chart there.
        /// </summary>
        private static JToken[] Slots(JToken token)
        {
            var slots = new JToken[ChartInfo.Difficulties];
            if (token.Type != JTokenType.Array)
            {
                for (int i = 0; i < slots.Length - 1; i++) slots[i] = JValue.CreateNull();
                slots[slots.Length - 1] = token;
                return slots;
            }

            for (int i = 0; i < slots.Length; i++) slots[i] = ((JArray)token)[i];
            return slots;
        }

        /// <summary>
        /// Whether the three keys say something consistent about each difficulty.
        ///
        /// A difficulty is either described — a chart to play, a designer and a rating to file with it
        /// — or it is not described at all, which is what three nulls say. The one exception is a
        /// difficulty with no chart: there, a designer and a rating are allowed to stand on their own,
        /// because an author who is still writing a chart may well know who is writing it and how hard
        /// it is. The other direction has no exception: a chart with no designer or no rating is a
        /// record the game would carry with a hole in it, and that is a file to refuse rather than to
        /// write half of.
        /// </summary>
        private static bool Aligned(JToken[] charts, JToken[] charters, JToken[] levels, out string reason)
        {
            reason = null;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (charts[d].Type == JTokenType.Null) continue;   // no chart: the two may stand alone

                if (charters[d].Type == JTokenType.Null || levels[d].Type == JTokenType.Null)
                {
#if DEBUG
                    reason = $"difficulty {d} has a chart but no "
                           + (charters[d].Type == JTokenType.Null ? "\"charter\"" : "\"lv\"");
#endif
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The four chart designers, in difficulty order — a null where the `if` file gave none.
        ///
        /// A difficulty with no chart may leave its designer out, which is the only place a null can
        /// stand here: <see cref="Aligned"/> has already refused a chart that names no designer.
        /// </summary>
        private static bool Texts(JToken[] slots, string key, out string[] values, out string reason)
        {
            values = null;
            reason = null;

            var result = new string[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JToken item = slots[i];
                if (item.Type == JTokenType.Null) continue;   // no chart there, so no designer to name

                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a name";
#endif
                    return false;
                }
                result[i] = item.Value<string>();
            }

            values = result;
            return true;
        }

        /// <summary>
        /// The four difficulty ratings, in difficulty order — a null where the `if` file gave none.
        ///
        /// The game stores these as small integers, and a rating of zero or less is not one: null is
        /// how "no rating" is written, and it is available only where there is no chart to rate
        /// (<see cref="Aligned"/>).
        /// </summary>
        private static bool Levels(JToken[] slots, string key, out int?[] values, out string reason)
        {
            values = null;
            reason = null;

            var result = new int?[ChartInfo.Difficulties];
            for (int i = 0; i < result.Length; i++)
            {
                JToken item = slots[i];
                if (item.Type == JTokenType.Null) continue;   // no chart there, so nothing to rate

                if (item.Type != JTokenType.Integer)
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is not a whole number";
#endif
                    return false;
                }

                int level = item.Value<int>();
                if (level <= 0)
                {
#if DEBUG
                    reason = $"\"{key}\"[{i}] is {level}";
#endif
                    return false;
                }
                result[i] = level;
            }

            values = result;
            return true;
        }

        /// <summary>
        /// The chart files, in difficulty order, with `null` for a difficulty that has none.
        ///
        /// Each named file has to be in the folder, and each has to be a chart the game's own decoder
        /// will accept — that second half is checked in <see cref="ChartLoaderMod"/>, which is where
        /// the decoder is reachable. A chartless difficulty names no file at all: nothing is read for
        /// it, nothing is served for it, and the difficulty is answered as "no chart" wherever the
        /// game asks about it.
        /// </summary>
        private static bool ChartFiles(JToken[] slots, string folder, out string[] paths,
                                       out string[] chartNames, out bool[] noChart, out string reason)
        {
            paths = null;
            chartNames = null;
            noChart = null;
            reason = null;

            var result = new string[ChartInfo.Difficulties];
            var names = new string[ChartInfo.Difficulties];
            var absent = new bool[ChartInfo.Difficulties];

            for (int i = 0; i < ChartInfo.Difficulties; i++)
            {
                JToken item = slots[i];
                if (item.Type == JTokenType.Null)
                {
                    absent[i] = true;
                    continue;
                }
                if (item.Type != JTokenType.String || string.IsNullOrWhiteSpace(item.Value<string>()))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] is not a name";
#endif
                    return false;
                }

                string name = item.Value<string>();
                if (Path.GetFileName(name) != name)
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] names something outside the folder: {name}";
#endif
                    return false;
                }

                string path = Path.Combine(folder, name);
                if (!File.Exists(path))
                {
#if DEBUG
                    reason = $"\"chart\"[{i}] points at {name}, which is not in the folder";
#endif
                    return false;
                }

                result[i] = path;
                names[i] = name;
            }

            paths = result;
            chartNames = names;
            noChart = absent;
            return true;
        }
    }
}
