using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>Adds the custom songs to the game's song and pack tables.</summary>
    internal static unsafe class SongCatalog
    {
        private static int SongDataAllSongs => Offsets.SongData.AllSongs;
        private static int SongDataAvailability => Offsets.SongData.Availability;

        /// <summary>`BitArray.m_array` and `m_length`.</summary>
        private const int BitArrayArray = Offsets.Runtime.BitArrayArray;
        private const int BitArrayLength = Offsets.Runtime.BitArrayLength;

        /// <summary>Grows the game's per-song `BitArray` to cover <paramref name="count"/> songs.</summary>
        private static bool GrowAvailability(IntPtr songData, int count)
        {
            IntPtr bits = Memory.Ptr(songData + SongDataAvailability);
            if (!Memory.LooksLikeObject(bits))
            {
                Diagnostics.Warn("SongData has no availability bits");
                return false;
            }

            int oldLength = Memory.I32(bits + BitArrayLength);
            if (oldLength >= count) return true;

            int oldWords = (oldLength + 31) / 32;
            int newWords = (count + 31) / 32;

            IntPtr words = Memory.Ptr(bits + BitArrayArray);
            if (!Memory.LooksLikeObject(words))
            {
                Diagnostics.Warn($"the availability bits have no backing array (0x{words.ToInt64():x})");
                return false;
            }

            IntPtr grown = NewArray(words, newWords);
            if (grown == IntPtr.Zero)
            {
                Diagnostics.Warn($"no room for {newWords} availability word(s)");
                return false;
            }

            long bytes = (long)Memory.I32(words + Offsets.Runtime.ArrayLength) * sizeof(int);
            Buffer.MemoryCopy((void*)(words + Offsets.Runtime.ArrayDataOffset),
                              (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);

            for (int i = 0; i < newWords; i++)
            {
                int mask = i < oldWords ? Memory.I32(words + Offsets.Runtime.ArrayDataOffset + i * sizeof(int))
                                        : 0;
                for (int b = 0; b < 32; b++)
                {
                    int index = i * 32 + b;
                    if (index < oldLength || index >= count) continue;
                    mask |= 1 << b;
                }
                Memory.WriteI32(grown + Offsets.Runtime.ArrayDataOffset + i * sizeof(int), mask);
            }

            Memory.WritePtr(bits + BitArrayArray, grown);
            Memory.WriteI32(bits + BitArrayLength, count);
            Diagnostics.Info($"availability bits grown from {oldLength} to {count}");
            return true;
        }
        private static int SongDataNameIndex => Offsets.SongData.NameIndex;

        /// <summary>The one prefix the game's rebuild leaves out.</summary>
        private const string FilteredPrefix = "tutorial";

        /// <summary>A ceiling on a title-reading array's length.</summary>
        private const int LocaleLimit = 64;

        private static int PackDataPacks => Offsets.PackData.Packs;

        private static int SongInfoSize => Offsets.Song.Size;
        private static int SongInfoId => Offsets.Song.Id;                  // SongId (ushort)
        private static int SongInfoBaseName => Offsets.Song.BaseName;      // string
        private static int SongInfoCharts => Offsets.Song.Charts;          // SongChartInfo[]
        private static int SongInfoPreviewStart => Offsets.Song.PreviewStart;
        private static int SongInfoPreviewEnd => Offsets.Song.PreviewEnd;
        private static int SongInfoTitleReading => Offsets.Song.TitleReading;
        private static int SongInfoArtistReading => Offsets.Song.ArtistReading;
        private static int SongInfoRewardStyle => Offsets.Song.RewardStyle;

        /// <summary>`RewardStyle.Supress`.</summary>
        private const int RewardStyleSupress = 0;

        // ---- SongChartInfo ----
        private static int ChartInfoSize => Offsets.Chart.Size;
        private static int ChartInfoId => Offsets.Chart.Id;                       // string
        private static int ChartInfoAvailable => Offsets.Chart.Available;         // bool
        private static int ChartInfoDifficulty => Offsets.Chart.Difficulty;       // ChartDifficultyFlag
        private static int ChartInfoDesigner => Offsets.Chart.Designer;           // string
        private static int ChartInfoJacketDesigner => Offsets.Chart.JacketDesigner;
        private static int ChartInfoRating => Offsets.Chart.Rating;               // int
        private static int ChartInfoSection => Offsets.Chart.Section;             // string

        // ---- PackInfo ----
        private static int PackInfoSize => Offsets.Pack.Size;
        private static int PackInfoId => Offsets.Pack.Id;        // PackId (ushort)
        private static int PackInfoSlug => Offsets.Pack.Slug;    // string
        private static int PackInfoSongs => Offsets.Pack.Songs;  // SongId[]

        /// <summary>
        /// Where in the pack array the custom packs are inserted. -1 means append:
        /// shipped packs keep their original list indices and visual rows.
        /// </summary>
        private const int CustomPackIndex = -1;

        private static int InsertIndex(int oldCount) =>
            CustomPackIndex >= 0 ? CustomPackIndex : oldCount;

        /// <summary>The game pack ids this run handed out.</summary>
        internal static readonly HashSet<int> CustomPackIds = new HashSet<int>();

        /// <summary>The custom packs, keyed by their game pack id.</summary>
        internal static readonly Dictionary<int, ChartPack> PacksById =
            new Dictionary<int, ChartPack>();

        /// <summary>The song ids this run handed out.</summary>
        internal static readonly HashSet<ushort> CustomSongIds = new HashSet<ushort>();

        /// <summary>The same songs, by id, with their `if` file data.</summary>
        internal static readonly Dictionary<ushort, ChartInfo> ById =
            new Dictionary<ushort, ChartInfo>();

        /// <summary>Which difficulty each song's illustrator entry currently shows.</summary>
        private static readonly Dictionary<ushort, int> IllustratorShown = new Dictionary<ushort, int>();

        /// <summary>
        /// The difficulty flags, in the order <c>chart[0..3]</c> is written: Minimal, Evolved,
        /// Ultimate, Forbidden.
        /// </summary>
        private static readonly byte[] DifficultyFlags = { 1, 2, 4, 8 };

        /// <summary>The index <c>chart[0..3]</c> a difficulty flag names, or -1 for anything else.</summary>
        internal static int DifficultyIndexOf(byte flag)
        {
            for (int i = 0; i < DifficultyFlags.Length; i++)
                if (DifficultyFlags[i] == flag) return i;
            return -1;
        }

        private static int _songDataField = int.MinValue;
        private static int _packDataField = int.MinValue;
        private static bool _unusable;

        /// <summary>Whether this run has to give up on the game's tables.</summary>
        internal static bool Unusable => _unusable;

        /// <summary>The two game data objects, or false while they are not loaded.</summary>
        internal static bool TryGetAssets(out IntPtr songData, out IntPtr packData)
        {
            songData = IntPtr.Zero;
            packData = IntPtr.Zero;

            if (_unusable) return false;

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            if (_songDataField == int.MinValue)
            {
                _songDataField = FieldResolver.Field("DataAccess", "_JAb");
                _packDataField = FieldResolver.Field("DataAccess", "_lAb");
            }

            if (_songDataField < 0 || _packDataField < 0)
            {
                _unusable = true;
                Diagnostics.Error("the game's data assets could not be found by name in this build; " +
                                  "no custom song will be registered");
                return false;
            }

            songData = Memory.Ptr(statics + _songDataField);
            packData = Memory.Ptr(statics + _packDataField);

            if (!Memory.LooksLikeObject(songData) || !Memory.LooksLikeObject(packData)) return false;

            if (!Offsets.Resolve(songData, packData))
            {
                _unusable = true;
                return false;
            }

            return true;
        }

        /// <summary>Settles every song's name and refuses the ones the game already has.</summary>
        internal static List<ChartInfo> SettleNames(List<ChartInfo> charts, IntPtr songData)
        {
            var admitted = new List<ChartInfo>(charts.Count);

            IntPtr songs = Memory.Ptr(songData + SongDataAllSongs);
            int count = Memory.LooksLikeObject(songs) ? Memory.I32(songs + Offsets.Runtime.ArrayLength) : 0;

            var taken = new HashSet<string>(StringComparer.Ordinal);

            const int SlotsToCheck = 4;
            int stride = SongInfoSize;
            long byteLength = Memory.ArrayBytes(songs);

            for (int i = 0; i < count && byteLength > 0 && (long)(i + 1) * stride <= byteLength; i++)
            {
                IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + i * stride;

                ushort id = Memory.U16(slot + SongInfoId);
                if (i < SlotsToCheck && id != 0 && id != (ushort)i)
                {
                    Diagnostics.Error($"the song table does not index the way this build expects " +
                                      $"(slot {i} carries id {id}, not {i}); no custom song was " +
                                      "registered, because their names cannot be checked against the game's");
                    return admitted;
                }

                string text = Memory.Text(Memory.Ptr(slot + SongInfoBaseName));
                if (text != null) taken.Add(text);
            }

            foreach (ChartInfo chart in charts)
            {
                if (taken.Contains(chart.BaseName))
                {
                    Diagnostics.WarnRelease($"'{chart.BaseName}' is already the name of a song this game " +
                                            $"has, so '{chart.FolderName}' was not loaded. Change the id " +
                                            "in its if file.");
                    continue;
                }

                taken.Add(chart.BaseName);
                admitted.Add(chart);

                ChartCatalog.Adopt(chart);
            }

            return admitted;
        }

        /// <summary>Adds every pack and its songs to the game's song and pack tables.</summary>
        internal static InjectionResult Inject(IntPtr songData, IntPtr packData, List<ChartPack> packs)
        {
            var result = new InjectionResult();
            if (packs == null || packs.Count == 0) return result;

            CustomPackIds.Clear();
            PacksById.Clear();
            CustomSongIds.Clear();
            ById.Clear();

            IntPtr oldSongs = Memory.Ptr(songData + SongDataAllSongs);
            if (!Memory.LooksLikeObject(oldSongs))
            {
                Diagnostics.Warn("SongData has no song array");
                return result;
            }

            int oldCount = Memory.I32(oldSongs + Offsets.Runtime.ArrayLength);
            if (oldCount <= 0)
            {
                Diagnostics.Warn("SongData has no songs to take a template from");
                return result;
            }

            IntPtr oldPacks = Memory.Ptr(packData + PackDataPacks);
            if (!Memory.LooksLikeObject(oldPacks))
            {
                Diagnostics.Warn("PackData has no pack array");
                return result;
            }

            int oldPackCount = Memory.I32(oldPacks + Offsets.Runtime.ArrayLength);
            if (oldPackCount <= 0)
            {
                Diagnostics.Warn("the pack list is empty; there is no pack list to extend");
                return result;
            }

            int totalSongs = 0;
            foreach (ChartPack pack in packs) totalSongs += pack.Charts.Count;
            if (totalSongs == 0) return result;

            int newCount = oldCount + totalSongs;

            if (!GrowAvailability(songData, newCount)) return result;

            IntPtr songs = GrowArray(oldSongs, newCount);
            if (songs == IntPtr.Zero) return result;

            IntPtr chartTemplate = FirstCharts(oldSongs, oldCount);
            if (chartTemplate == IntPtr.Zero) return result;

            IntPtr titleTemplate = FirstReadings(oldSongs, oldCount);
            int packIdBase = NextPackId(oldPacks, oldPackCount);

            var songIds = new IntPtr[packs.Count];
            for (int i = 0; i < packs.Count; i++)
            {
                songIds[i] = AllocateSongIds(packData, packs[i].Charts.Count);
                if (songIds[i] == IntPtr.Zero) return result;
            }

            int nextSong = oldCount;
            for (int p = 0; p < packs.Count; p++)
            {
                ChartPack pack = packs[p];
                int start = nextSong;

                for (int i = 0; i < pack.Charts.Count; i++)
                {
                    ChartInfo info = pack.Charts[i];
                    int id = nextSong;
                    CustomSongIds.Add((ushort)id);
                    ById[(ushort)id] = info;
                    IntPtr slot = songs + Offsets.Runtime.ArrayDataOffset + nextSong * SongInfoSize;

                    Buffer.MemoryCopy((void*)(oldSongs + Offsets.Runtime.ArrayDataOffset), (void*)slot,
                                      SongInfoSize, SongInfoSize);

                    Memory.WritePtr(slot + SongInfoTitleReading, BuildReadings(titleTemplate, info.Name));
                    Memory.WritePtr(slot + SongInfoArtistReading, Str(info.Composer));

                    Memory.WriteU16(slot + SongInfoId, (ushort)id);
                    Memory.WritePtr(slot + SongInfoBaseName, Str(info.BaseName));

                    Memory.WriteF32(slot + SongInfoPreviewStart, info.PreviewStart);
                    Memory.WriteF32(slot + SongInfoPreviewEnd, info.PreviewEnd);

                    Memory.WriteI32(slot + SongInfoRewardStyle, RewardStyleSupress);

                    IntPtr chartInfos = BuildCharts(chartTemplate, info);
                    Memory.WritePtr(slot + SongInfoCharts, chartInfos);
                    JacketCatalog.RegisterCharts(chartInfos, info);
                    nextSong++;
                }

                for (int i = 0; i < pack.Charts.Count; i++)
                    WriteSongId(songIds[p], i, (ushort)(start + i));

                int idForText = start;
                foreach (ChartInfo info in pack.Charts)
                {
                    PackSetup.SetSongText(PackSetup.SongText.Title, idForText, info.Name);
                    PackSetup.SetSongText(PackSetup.SongText.Artist, idForText, info.Composer);
                    PackSetup.SetSongText(PackSetup.SongText.Illustrator, idForText,
                                          IllustratorOf(info, 0));
                    idForText++;
                }
            }

            Memory.WritePtr(songData + SongDataAllSongs, songs);

            IntPtr packsArray = AddPacks(oldPacks, oldPackCount, packs, songIds, packIdBase,
                                         out int packsAdded, out int customAdded, out int legacyAdded);
            if (packsArray == IntPtr.Zero)
            {
                Diagnostics.Warn("the custom packs could not be added to the pack table");
                return result;
            }

            Memory.WritePtr(packData + PackDataPacks, packsArray);
            PackSetup.ApplyCategories(packData, packs);
            result.SongsAdded = nextSong - oldCount;
            result.PacksAdded = packsAdded;
            result.CustomPacksAdded = customAdded;
            result.LegacyPacksAdded = legacyAdded;

            Refresh(songData);
            return result;
        }

        /// <summary>The illustrator to show for one difficulty.</summary>
        private static string IllustratorOf(ChartInfo info, int difficulty) =>
            info.Illustrators == null ? string.Empty : info.Illustrators[difficulty];

        /// <summary>Whether the four illustrators are not all the same.</summary>
        private static bool VariesByDifficulty(ChartInfo info)
        {
            string[] names = info.Illustrators;
            if (names == null) return false;

            for (int i = 1; i < names.Length; i++)
                if (names[i] != names[0]) return true;
            return false;
        }

        /// <summary>Puts the applied difficulty's illustrator into the localisation entry.</summary>
        internal static void ShowIllustrator(ushort songId, byte difficultyFlag)
        {
            int difficulty = DifficultyIndexOf(difficultyFlag);

            if (difficulty < 0) return;
            if (!ById.TryGetValue(songId, out ChartInfo info)) return;
            if (!VariesByDifficulty(info)) return;
            if (IllustratorShown.TryGetValue(songId, out int shown) && shown == difficulty) return;

            if (PackSetup.SetSongText(PackSetup.SongText.Illustrator, songId, info.Illustrators[difficulty]))
                IllustratorShown[songId] = difficulty;
        }

        /// <summary>Makes the game re-derive the lists it builds from <c>allSongInfo</c>.</summary>
        private static void Refresh(IntPtr songData)
        {
            Memory.WritePtr(songData + SongDataNameIndex, IntPtr.Zero);

            IntPtr klass = FieldResolver.ClassPointer("SongData");
            if (klass == IntPtr.Zero) { Diagnostics.Error("SongData could not be found; the new songs will not show"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_yOA", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Error("SongData._yOA could not be found; the new songs will not show");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, songData, null, ref raised);

            if (raised != IntPtr.Zero)
                Diagnostics.Error($"the game's song list refresh raised: {Raised.Text(raised)}");

            Diagnostics.Info("song list rebuilt");
        }

        /// <summary>A title-reading array with the same text in every locale.</summary>
        private static IntPtr BuildReadings(IntPtr template, string text)
        {
            if (!Memory.LooksLikeObject(template)) return IntPtr.Zero;

            int locales = Memory.I32(template + Offsets.Runtime.ArrayLength);
            if (locales <= 0 || locales > LocaleLimit)
            {
                Diagnostics.Warn($"the shipped title readings hold no locale count ({locales}); " +
                                 $"'{text}' keeps its name from the localisation table");
                return IntPtr.Zero;
            }

            IntPtr array = NewArray(template, locales);
            if (array == IntPtr.Zero) return IntPtr.Zero;

            IntPtr value = Str(text);
            for (int i = 0; i < locales; i++)
                Memory.WritePtr(array + Offsets.Runtime.ArrayDataOffset + i * IntPtr.Size, value);

            return array;
        }

        /// <summary>The per-difficulty records for one song.</summary>
        private static IntPtr BuildCharts(IntPtr template, ChartInfo info)
        {
            IntPtr array = NewArray(template, ChartInfo.Difficulties);
            if (array == IntPtr.Zero) return IntPtr.Zero;

            IntPtr source = template + Offsets.Runtime.ArrayDataOffset;

            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                IntPtr slot = array + Offsets.Runtime.ArrayDataOffset + d * ChartInfoSize;
                Buffer.MemoryCopy((void*)source, (void*)slot, ChartInfoSize, ChartInfoSize);

                if (info.NoChart[d])
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(info.BaseName + "_" + (d + 1)));
                    Memory.WriteU8(slot + ChartInfoAvailable, 0);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }
                else
                {
                    Memory.WritePtr(slot + ChartInfoId, Str(Path.GetFileName(info.ChartPaths[d])));
                    Memory.WriteU8(slot + ChartInfoAvailable, 1);
                    Memory.WriteU8(slot + ChartInfoDifficulty, DifficultyFlags[d]);
                }

                Memory.WritePtr(slot + ChartInfoDesigner, Str(info.Charters[d] ?? string.Empty));

                Memory.WritePtr(slot + ChartInfoJacketDesigner, Str(IllustratorOf(info, d)));

                int? level = info.Level[d];
                Memory.WriteI32(slot + ChartInfoRating, level ?? 0);
                Memory.WritePtr(slot + ChartInfoSection,
                                Str(level is int rated
                                    ? rated.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                    : string.Empty));
            }

            return array;
        }

        /// <summary>Inserts all custom packs before the game's shown packs.</summary>
        private static IntPtr AddPacks(IntPtr oldPacks, int oldCount, List<ChartPack> packs,
                                       IntPtr[] songIds, int packIdBase,
                                       out int packsAdded, out int customAdded, out int legacyAdded)
        {
            packsAdded = 0;
            customAdded = 0;
            legacyAdded = 0;

            int newCount = oldCount + packs.Count;
            IntPtr grown = NewArray(oldPacks, newCount);
            if (grown == IntPtr.Zero) return IntPtr.Zero;

            IntPtr source = oldPacks + Offsets.Runtime.ArrayDataOffset;
            IntPtr target = grown + Offsets.Runtime.ArrayDataOffset;

            int insert = InsertIndex(oldCount);
            long head = (long)insert * PackInfoSize;
            if (head > 0) Buffer.MemoryCopy((void*)source, (void*)target, head, head);

            IntPtr cursor = (IntPtr)(target.ToInt64() + head);
            for (int i = 0; i < packs.Count; i++)
            {
                ChartPack pack = packs[i];
                int packId = packIdBase + i;

                Memory.WriteU16(cursor + PackInfoId, (ushort)packId);
                Memory.WritePtr(cursor + PackInfoSlug, Str(PackSlug(pack)));
                Memory.WritePtr(cursor + PackInfoSongs, songIds[i]);

                pack.GamePackId = packId;
                pack.VisualRow = -1;
                CustomPackIds.Add(packId);
                PacksById[packId] = pack;

                packsAdded++;
                if (pack.IsLegacy) legacyAdded++;
                else customAdded++;

                Diagnostics.Info($"pack '{pack.LogicalId}' added at index {insert + i} " +
                                 $"(id {packId}) with {pack.Charts.Count} songs");
                cursor = (IntPtr)(cursor.ToInt64() + PackInfoSize);
            }

            long rest = (long)(oldCount - insert) * PackInfoSize;
            if (rest > 0)
            {
                Buffer.MemoryCopy((void*)(source.ToInt64() + head),
                                  (void*)cursor.ToInt64(), rest, rest);
            }

            return grown;
        }

        /// <summary>The stable game slug for one pack.</summary>
        private static string PackSlug(ChartPack pack) =>
            pack.IsLegacy ? "IFCL" : "custom_" + pack.LogicalId;

        /// <summary>The first pack id the game is not using.</summary>
        private static int NextPackId(IntPtr packs, int count)
        {
            int next = 0;

            for (int i = 0; i < count; i++)
            {
                int id = Memory.U16(packs + Offsets.Runtime.ArrayDataOffset + i * PackInfoSize + PackInfoId);
                if (id >= next) next = id + 1;
            }

            return next;
        }

        /// <summary>A `SongId[]` holding the ids this mod handed out.</summary>
        private static IntPtr AllocateSongIds(IntPtr packData, int count)
        {
            IntPtr packs = Memory.Ptr(packData + PackDataPacks);
            int packCount = Memory.I32(packs + Offsets.Runtime.ArrayLength);
            if (packCount <= 0) { Diagnostics.Warn("PackData has no packs to take a template from"); return IntPtr.Zero; }

            IntPtr firstPack = packs + Offsets.Runtime.ArrayDataOffset;
            IntPtr template = Memory.Ptr(firstPack + PackInfoSongs);
            if (!Memory.LooksLikeObject(template)) { Diagnostics.Warn("a shipped pack has no song ids"); return IntPtr.Zero; }

            return NewArray(template, count);
        }

        /// <summary>The first song's `ChartInfos` array object.</summary>
        private static IntPtr FirstCharts(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr charts = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset + i * SongInfoSize + SongInfoCharts);
                if (Memory.LooksLikeObject(charts) &&
                    Memory.I32(charts + Offsets.Runtime.ArrayLength) >= ChartInfo.Difficulties)
                    return charts;
            }

            Diagnostics.Warn("no shipped song has four difficulty records to take a shape from");
            return IntPtr.Zero;
        }

        /// <summary>The first shipped title-reading array.</summary>
        private static IntPtr FirstReadings(IntPtr songs, int count)
        {
            for (int i = 0; i < count; i++)
            {
                IntPtr readings = Memory.Ptr(songs + Offsets.Runtime.ArrayDataOffset +
                                             i * SongInfoSize + SongInfoTitleReading);
                if (Memory.LooksLikeObject(readings) &&
                    Memory.I32(readings + Offsets.Runtime.ArrayLength) > 0)
                    return readings;
            }

            Diagnostics.Warn("no shipped song carries a title-reading array to take a shape from");
            return IntPtr.Zero;
        }

        /// <summary>An array of the same element type, larger, with the old contents copied.</summary>
        internal static IntPtr GrowArray(IntPtr like, int count)
        {
            int oldCount = Memory.I32(like + Offsets.Runtime.ArrayLength);
            IntPtr grown = NewArray(like, count);
            if (grown == IntPtr.Zero) return IntPtr.Zero;

            int stride = ElementStride(like, oldCount);
            if (stride <= 0) return IntPtr.Zero;

            long bytes = (long)oldCount * stride;
            if (bytes > 0)
            {
                Buffer.MemoryCopy((void*)(like + Offsets.Runtime.ArrayDataOffset),
                                  (void*)(grown + Offsets.Runtime.ArrayDataOffset), bytes, bytes);
            }

            return grown;
        }

        /// <summary>An array of the same element type as <paramref name="like"/>, empty.</summary>
        internal static IntPtr NewArray(IntPtr like, int count)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(like);
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element == IntPtr.Zero) { Diagnostics.Warn("an array has no element class"); return IntPtr.Zero; }

            IntPtr array = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, (ulong)count);
            if (array == IntPtr.Zero) Diagnostics.Warn($"could not allocate an array of {count}");
            return array;
        }

        private static int ElementStride(IntPtr array, int count)
        {
            if (count <= 0) return 0;
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);
            return (int)(bytes / count);
        }

        private static void WriteSongId(IntPtr array, int index, ushort value) =>
            Memory.WriteU16(array + Offsets.Runtime.ArrayDataOffset + index * 2, value);

        /// <summary>A managed string as an IL2CPP one.</summary>
        private static IntPtr Str(string text) =>
            Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(text);
    }
}
