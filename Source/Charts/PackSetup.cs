using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The pack's row of visuals and its reverse lookup.</summary>
    internal static unsafe class PackSetup
    {
        /// <summary>`PackSelectSceneAssets.packToAssets`.</summary>
        private static int PackAssetsTable = Offsets.Unresolved;

        /// <summary>`DataAccess._NAb`.</summary>
        private static int PackAssetsField = Offsets.Unresolved;

        /// <summary>`DataAccess._mAb` — the localisation table.</summary>
        private static int StringMappingField = Offsets.Unresolved;

        /// <summary>The pack visuals, or false while the game has not loaded them.</summary>
        internal static bool TryGetVisuals(out IntPtr packAssets)
        {
            packAssets = IntPtr.Zero;

            if (!ResolveFields()) return false;

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            packAssets = Memory.Ptr(statics + PackAssetsField);
            return Memory.LooksLikeObject(packAssets);
        }

        /// <summary>Gives one custom pack a row in the visual table.</summary>
        internal static bool ExtendVisuals(IntPtr packAssets, ChartPack pack)
        {
            if (pack.GamePackId < 0) return false;

            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table))
            {
                Diagnostics.Warn("the pack visual table is missing");
                NoRow();
                return false;
            }

            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int rowSize = count > 0 ? (int)(bytes / count) : 0;
            if (rowSize <= 0)
            {
                Diagnostics.Warn("the pack visual table has no element size");
                NoRow();
                return false;
            }

            // packToAssets is indexed by PackId. Custom ids start above the shipped
            // ids; fill any gap (notably the shipped id 8 row) with the source row
            // instead of letting a custom pack occupy a shipped id's slot.
            int packIndex = pack.GamePackId;
            int shippedCount = count;
            if (packIndex >= count)
            {
                int grownCount = packIndex + 1;
                IntPtr grown = SongCatalog.GrowArray(table, grownCount);
                if (grown == IntPtr.Zero)
                {
                    Diagnostics.Warn("the pack visual table could not be grown");
                    NoRow();
                    return false;
                }

                table = grown;
                count = grownCount;
                Memory.WritePtr(packAssets + PackAssetsTable, grown);
            }

            int from = Hooks.SourceRow(table, shippedCount, rowSize);
            if (from < 0)
            {
                Diagnostics.Warn("no shipped pack row has a material to copy");
                NoRow();
                return false;
            }

            IntPtr source = (IntPtr)(table.ToInt64() + Offsets.Runtime.ArrayDataOffset + from * rowSize);
            IntPtr target = (IntPtr)(table.ToInt64() + Offsets.Runtime.ArrayDataOffset + packIndex * rowSize);

            unsafe
            {
                // Newly grown rows are uninitialized. Give each one the source row
                // first; the custom pack then overwrites only its own PackId slot.
                for (int row = shippedCount; row < count; row++)
                {
                    IntPtr fill = (IntPtr)(table.ToInt64() + Offsets.Runtime.ArrayDataOffset + row * rowSize);
                    Buffer.MemoryCopy((void*)source, (void*)fill, rowSize, rowSize);
                }

                Buffer.MemoryCopy((void*)source, (void*)target, rowSize, rowSize);
            }

            pack.VisualRow = packIndex;
            pack.HasCustomCover = false;
            pack.CoverMaterial = IntPtr.Zero;
            if (CoversEnabled && pack.CoverPath != null)
            {
                IntPtr cover = JacketFactory.Build(pack.CoverPath, "pack cover", out string reason);
                if (cover == IntPtr.Zero)
                {
                    Diagnostics.Warn($"pack cover '{pack.CoverPath}' could not be built: {reason}");
                }
                else
                {
                    pack.CoverMaterial = cover;
                    if (ApplyCover(source, target, cover))
                        pack.HasCustomCover = true;
                }
            }

            Diagnostics.Info($"pack '{pack.LogicalId}' visual row {packIndex} copied from row {from}" +
                             (pack.HasCustomCover ? " with custom cover" : ""));
            return true;
        }

        /// <summary>Clones the shipped materials and puts the pack cover on them.</summary>
        private static bool ApplyCover(IntPtr sourceRow, IntPtr targetRow, IntPtr cover)
        {
            string reason = null;
            foreach (string field in CoverMaterialFields)
            {
                int offset = FieldResolver.Field("SongSelectPackAssets", field);
                if (offset < 0)
                {
                    Diagnostics.Warn($"the pack visual field {field} could not be resolved");
                    return false;
                }

                IntPtr source = Memory.Ptr(sourceRow + offset);
                IntPtr material = Memory.LooksLikeObject(source)
                    ? BackgroundClone.Build(source, cover, out reason)
                    : cover;

                if (material == IntPtr.Zero)
                {
                    Diagnostics.Warn($"the pack cover could not be applied to {field}: {reason}");
                    return false;
                }

                Memory.WritePtr(targetRow + offset, material);
            }

            return true;
        }

        /// <summary>The pack-card states that receive the cover.</summary>
        private static readonly string[] CoverMaterialFields =
        {
            "SongSelectLargeNonCompleted",
            "SongSelectLargeNonCompletedHover",
            "SongSelectLargeCompleted",
            "SongSelectLargeCompletedHover",
            "PackSelectLargeNonCompleted",
            "PackSelectLargeNonCompletedHover",
            "PackSelectLargeCompleted",
            "PackSelectLargeCompletedHover",
            "PackSelectNonCompleted",
            "PackSelectNonCompletedHover",
            "PackSelectCompleted",
            "PackSelectCompletedHover",
            "DLCSmallDefaultBacking",
            "DLCSmallHoverBacking",
            "UnownedSmallDefaultBannerVisual",
            "UnownedSmallHoverBannerVisual",
            "UnownedHubDefaultBannerVisual",
            "UnownedHubHoverBannerVisual",
        };

        /// <summary>Cover application is deliberately suspended while the row layout is investigated.</summary>
        private const bool CoversEnabled = false;

        private static bool _noRowReported;

        /// <summary>Reports that the custom pack has no row of visuals, once.</summary>
        internal static void NoRow()
        {
            if (_noRowReported) return;
            _noRowReported = true;
            Diagnostics.WarnRelease("the custom pack has no row of visuals of its own; it will wear " +
                                    "whatever this build draws for it");
        }

        /// <summary>The pack visuals table's offset.</summary>
        internal static int VisualsTableOffset => PackAssetsTable;

        /// <summary>Files each custom pack under the story or song column.</summary>
        internal static void ApplyCategories(IntPtr packData, List<ChartPack> packs)
        {
            int setField = FieldResolver.Field("PackData", "_FYA");
            if (setField < 0)
            {
                Diagnostics.WarnRelease("the pack category table could not be found; custom packs stay in " +
                                        "the story column");
                return;
            }

            IntPtr set = Memory.Ptr(packData + setField);
            if (!Memory.LooksLikeObject(set))
            {
                Diagnostics.WarnRelease("the pack category table is empty; custom packs stay in the story " +
                                        "column");
                return;
            }

            IntPtr klass = FieldResolver.ClassPointer("PackData");
            IntPtr owned = klass == IntPtr.Zero ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_UkA", 2);
            if (owned == IntPtr.Zero)
            {
                Diagnostics.WarnRelease("the pack ownership setter could not be found; custom packs stay " +
                                        "in the story column");
                return;
            }

            foreach (ChartPack pack in packs)
            {
                if (pack.GamePackId < 0) continue;

                ushort id = (ushort)pack.GamePackId;
                bool song = pack.Category == PackCategory.Song;

                if (!SetMembership(set, id, song))
                {
                    Diagnostics.WarnRelease($"pack '{pack.LogicalId}' could not be filed under the " +
                                            $"{(song ? "song" : "story")} column");
                    continue;
                }

                SetOwned(packData, owned, id, true);
                Diagnostics.Info($"pack '{pack.LogicalId}' category={(song ? "song" : "story")} owned=true");
            }
        }

        /// <summary>Adds or removes one pack id in `PackData._FYA`.</summary>
        private static bool SetMembership(IntPtr set, ushort id, bool member)
        {
            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(set);
            if (klass == IntPtr.Zero) return false;

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                klass, member ? "Add" : "Remove", 1);
            if (method == IntPtr.Zero) return false;

            ushort value = id;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, set, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
            {
                Diagnostics.WarnRelease("the pack category update raised: " + Raised.Text(raised));
                return false;
            }

            return true;
        }

        /// <summary>Sets the DLC/ownership flag the category methods read.</summary>
        private static void SetOwned(IntPtr packData, IntPtr method, ushort id, bool owned)
        {
            ushort value = id;
            byte flag = (byte)(owned ? 1 : 0);
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&value);
            args[1] = (IntPtr)(&flag);
            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, packData, (void**)args, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.WarnRelease("the pack ownership update raised: " + Raised.Text(raised));
        }

        /// <summary>Rebuilds the game's song-to-pack lookup.</summary>
        internal static void RefreshLookup(IntPtr packData)
        {
            IntPtr klass = FieldResolver.ClassPointer("PackData");
            if (klass == IntPtr.Zero) { Diagnostics.Warn("PackData could not be found; the pack lookup is stale"); return; }

            IntPtr method = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, "_SkA", 0);
            if (method == IntPtr.Zero)
            {
                Diagnostics.Warn("PackData._SkA could not be found; the pack lookup is stale");
                return;
            }

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(method, packData, null, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Warn($"the game's pack lookup rebuild raised: {Raised.Text(raised)}");

            Diagnostics.Info("pack lookup rebuilt");
        }

        /// <summary>`DynamicStringMapping.packIdTypeMapping`.</summary>
        private static int PackIdTypeMapping = Offsets.Unresolved;

        /// <summary>The three song text mappings.</summary>
        private static int SongTitleTypeMapping = Offsets.Unresolved;
        private static int SongArtistTypeMapping = Offsets.Unresolved;
        private static int SongIllustratorTypeMapping = Offsets.Unresolved;

        private static bool _fieldsResolved;

        private static bool _fieldsMissing;

        /// <summary>Resolves the fields this file uses.</summary>
        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            if (FieldResolver.ClassPointer("DataAccess") == IntPtr.Zero) return false;

            PackAssetsTable = FieldResolver.Field("PackSelectSceneAssets", "packToAssets");

            PackIdTypeMapping = FieldResolver.Field("DynamicStringMapping", "packIdTypeMapping");
            SongTitleTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdTitleTypeMapping");
            SongArtistTypeMapping = FieldResolver.Field("DynamicStringMapping", "songIdArtistTypeMapping");
            SongIllustratorTypeMapping = FieldResolver.Field("DynamicStringMapping",
                                                             "jacketIllustratorNameTypeMapping");

            PackAssetsField = FieldResolver.Field("DataAccess", "_NAb");
            StringMappingField = FieldResolver.Field("DataAccess", "_mAb");

            if (PackAssetsTable < 0 || PackIdTypeMapping < 0 || SongTitleTypeMapping < 0 ||
                SongArtistTypeMapping < 0 || SongIllustratorTypeMapping < 0 ||
                PackAssetsField < 0 || StringMappingField < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the pack's fields could not be read by name in this build; the custom " +
                                  "pack keeps the game's own visuals and text");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        /// <summary>The five language slots of a `TextMappingValues`, in game order.</summary>
        private static readonly int TextMappingValuesSize = 5 * IntPtr.Size;

        /// <summary>
        /// `Mapping`'s offset once asked, `int.MinValue` before, -1 when it could not be asked.
        /// See <see cref="MappingOffset"/>.
        /// </summary>
        private static int _mappingOffset = int.MinValue;

        /// <summary>Gives the pack a name.</summary>
        private enum TextWhat { PackName, SongText }

        internal static bool SetPackName(int packId, string[] names)
        {
            // The field is read here, after `ResolveFields` �?not at the call site, for the reason
            // `SongText` gives.
            if (!ResolveFields()) return false;

            return SetTextValues(PackIdTypeMapping, packId, names, TextWhat.PackName);
        }

        /// <summary>Which text write this is.</summary>
        internal enum SongText { Title, Artist, Illustrator }

        /// <summary>Gives a song a title, artist, or illustrator.</summary>
        internal static bool SetSongText(SongText which, int songId, string text)
        {
            if (!ResolveFields()) return false;

            int mappingField = which == SongText.Title ? SongTitleTypeMapping
                             : which == SongText.Artist ? SongArtistTypeMapping
                             : SongIllustratorTypeMapping;

            return SetText(mappingField, songId, text, TextWhat.SongText);
        }

        /// <summary>The one-value write used by song texts.</summary>
        private static bool SetText(int mappingField, int id, string text, TextWhat what)
        {
            var names = new string[PackManifest.Languages];
            for (int i = 0; i < names.Length; i++) names[i] = text;
            return SetTextValues(mappingField, id, names, what);
        }

        /// <summary>Writes the five language slots of one mapping entry.</summary>
        private static bool SetTextValues(int mappingField, int id, string[] names, TextWhat what)
        {
            if (!ResolveFields()) return false;

            IntPtr klass = FieldResolver.ClassPointer("DataAccess");
            if (klass == IntPtr.Zero) return false;

            IntPtr statics = FieldResolver.Statics(klass);
            if (statics == IntPtr.Zero) return false;

            IntPtr mapping = Memory.Ptr(statics + StringMappingField);
            if (!Memory.LooksLikeObject(mapping))
            {
                NotWritten(what, "the localisation table is missing");
                return false;
            }

            IntPtr typeMapping = Memory.Ptr(mapping + mappingField);
            if (!Memory.LooksLikeObject(typeMapping))
            {
                NotWritten(what, $"the localisation mapping at +0x{mappingField:X} is missing");
                return false;
            }

            int at = MappingOffset(typeMapping);
            if (at < 0) return false;      // asked and reported where it was asked

            IntPtr table = Memory.Ptr(typeMapping + at);
            if (!Memory.LooksLikeObject(table))
            {
                NotWritten(what, $"the localisation table at +0x{mappingField:X} is missing");
                return false;
            }

            if (!Insert(table, id, names)) return false;

            Diagnostics.Info($"localisation +0x{mappingField:X}: entry {id} set to " +
                             $"{string.Join("/", names)}");
            return true;
        }

        /// <summary>Says which write could not be made.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void NotWritten(TextWhat what, string where) =>
            Diagnostics.Warn($"{where}; {Name(what)}");

        /// <summary>The two writes by name. Null in a Release build — see <see cref="TextWhat"/>.</summary>
        private static string Name(TextWhat what)
        {
#if DEBUG
            return what == TextWhat.PackName ? "the custom pack will have no name"
                                             : "the song will have no text of its own";
#else
            return null;
#endif
        }

        /// <summary>Where the mapping field sits.</summary>
        private static int MappingOffset(IntPtr typeMapping)
        {
            if (_mappingOffset != int.MinValue) return _mappingOffset;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(typeMapping);
            if (klass != IntPtr.Zero)
            {
                IntPtr field = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, "Mapping");
                if (field != IntPtr.Zero)
                {
                    int offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field);
                    if (offset > 0)
                    {
                        Diagnostics.Info($"StringTypeMapping.Mapping is at 0x{offset:X}");
                        return _mappingOffset = offset;
                    }
                }
            }

            foreach (var entry in InteropTypeIndex.ByName())
            {
                if (!entry.Key.Contains("StringTypeMapping")) continue;
                if (entry.Key.IndexOf('`') >= 0) continue;

                int offset = FieldResolver.Lookup(entry.Key, "Mapping");
                if (offset > 0)
                {
                    Diagnostics.Info($"StringTypeMapping.Mapping is at 0x{offset:X} (via {entry.Key})");
                    return _mappingOffset = offset;
                }
            }

            Diagnostics.Error("StringTypeMapping.Mapping could not be found on its own class in this " +
                              "build; the custom pack's texts will not be written");
            return _mappingOffset = -1;
        }

        /// <summary>Writes one entry in the text table.</summary>
        private static unsafe bool Insert(IntPtr table, int packId, string[] names)
        {
            ushort id = (ushort)packId;

            byte[] raw = new byte[TextMappingValuesSize];
            fixed (byte* values = raw)
            {
                for (int i = 0; i < PackManifest.Languages; i++)
                {
                    string name = i < names.Length ? names[i] : null;
                    IntPtr text = name == null
                        ? IntPtr.Zero
                        : Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
                    *(IntPtr*)(values + i * IntPtr.Size) = text;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)values;

                IntPtr dictClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(table);
                IntPtr setItem = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(
                    dictClass, "set_Item", 2);
                if (setItem == IntPtr.Zero)
                {
                    Diagnostics.Warn("the pack name table has no setter; the custom pack will have no name");
                    return false;
                }

                IntPtr raised = IntPtr.Zero;
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(setItem, table, (void**)args, ref raised);
                if (raised != IntPtr.Zero)
                {
                    Diagnostics.Warn($"writing the pack name raised: {Raised.Text(raised)}");
                    return false;
                }
            }

            return true;
        }
    }
}
