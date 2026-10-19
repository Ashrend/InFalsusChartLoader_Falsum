using System;

namespace InFalsusChartLoader
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

        /// <summary>Gives the new pack a row in the visual table.</summary>
        internal static bool ExtendVisuals(IntPtr packAssets, int packIndex)
        {
            IntPtr table = Memory.Ptr(packAssets + PackAssetsTable);
            if (!Memory.LooksLikeObject(table))
            {
                Diagnostics.Warn("the pack visual table is missing");
                NoRow();
                return false;
            }

            int count = Memory.I32(table + Offsets.Runtime.ArrayLength);
            if (packIndex < count) return true;   // already room; nothing to do

            long bytes = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table);
            int stride = count > 0 ? (int)(bytes / count) : 0;
            if (stride <= 0)
            {
                Diagnostics.Warn("the pack visual table has no element size");
                NoRow();
                return false;
            }

            IntPtr grown = SongCatalog.GrowArray(table, packIndex + 1);
            if (grown == IntPtr.Zero)
            {
                Diagnostics.Warn("the pack visual table could not be grown");
                NoRow();
                return false;
            }

            int from = Hooks.SourceRow(table, count, stride);
            if (from < 0)
            {
                Diagnostics.Warn("no shipped pack row has a material to copy");
                NoRow();
                return false;
            }

            unsafe
            {
                Buffer.MemoryCopy(
                    (void*)(table + Offsets.Runtime.ArrayDataOffset + from * stride),
                    (void*)(grown + Offsets.Runtime.ArrayDataOffset + packIndex * stride),
                    stride, stride);
            }

            Memory.WritePtr(packAssets + PackAssetsTable, grown);
            Diagnostics.Info($"pack visuals extended to {packIndex + 1} rows, " +
                             $"row {packIndex} copying row {from}, {stride} bytes");
            return true;
        }

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

        /// <summary>The five language slots of a `TextMappingValues`.</summary>
        private static readonly int TextMappingValuesSize = 5 * IntPtr.Size;

        private static readonly int ValueEnglish = 0 * IntPtr.Size;
        private static readonly int ValueJapanese = 1 * IntPtr.Size;
        private static readonly int ValueTraditionalChinese = 3 * IntPtr.Size;
        private static readonly int ValueSimplifiedChinese = 4 * IntPtr.Size;

        /// <summary>
        /// `Mapping`'s offset once asked, `int.MinValue` before, -1 when it could not be asked.
        /// See <see cref="MappingOffset"/>.
        /// </summary>
        private static int _mappingOffset = int.MinValue;

        /// <summary>Gives the pack a name.</summary>
        private enum TextWhat { PackName, SongText }

        internal static bool SetPackName(int packId, string name)
        {
            // The field is read here, after `ResolveFields` — not at the call site, for the reason
            // `SongText` gives.
            if (!ResolveFields()) return false;

            return SetText(PackIdTypeMapping, packId, name, TextWhat.PackName);
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

        /// <summary>The one text write.</summary>
        private static bool SetText(int mappingField, int id, string text, TextWhat what)
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

            if (!Insert(table, id, text)) return false;

            Diagnostics.Info($"localisation +0x{mappingField:X}: entry {id} set to '{text}'");
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
        private static unsafe bool Insert(IntPtr table, int packId, string name)
        {
            ushort id = (ushort)packId;

            byte[] raw = new byte[TextMappingValuesSize];
            fixed (byte* values = raw)
            {
                IntPtr text = Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp(name);
                *(IntPtr*)(values + ValueEnglish) = text;
                *(IntPtr*)(values + ValueJapanese) = text;
                *(IntPtr*)(values + ValueTraditionalChinese) = text;
                *(IntPtr*)(values + ValueSimplifiedChinese) = text;

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
