using System;
using System.Collections.Generic;

namespace InFalsusChartLoader
{
    /// <summary>
    /// The song ids stored in the custom results' keys, rewritten for the ids this run hands out.
    ///
    /// <para>
    /// A `SongId` is not a name: it is the index of the song's entry in `SongData.allSongInfo`, and a
    /// custom song's entry is appended after the shipped ones — so every game update that adds
    /// shipped songs moves it. A stored record is keyed by (`BaseName`, `SongId`, `Difficulty`) and
    /// the table's lookups hash all three, so a record saved by an earlier build sits under a key
    /// this build never asks about: the score reads as missing while it is still in the file.
    /// </para>
    /// <para>
    /// So after the file is read, the stored keys are copied out with the game's own reader (the
    /// dictionary's `Keys`, then `KeyCollection.CopyTo` — it walks the entry array, skips the empty
    /// slots and copies each key whole, at this build's layout), and every key whose song is one of
    /// this mod's is moved to the id this run gave that song: `Remove(key, out value)` takes the
    /// record off the old key, `set_Item` puts it under the new one. Taken and re-inserted rather
    /// than edited in place, because an entry carries its own hash and one whose key changed
    /// underneath it can no longer be found. Both calls are unconditional — a dictionary holds one
    /// entry per key, so a record already under the new id is overwritten — and the record's own
    /// `SongId` is written with the key's, so a row cannot contradict it.
    /// </para>
    /// <para>
    /// The play history is not touched. A row has no name, so after a shift a history row of this
    /// mod's cannot be told from a shipped song's — and nothing needs to: every reader of the
    /// history (the `_uG` predicates) reads a row's clear byte, its pace word and its judgement
    /// counts, never the id at its start.
    /// </para>
    /// <para>
    /// A chain that will not resolve is reported by name and the records are left as they are: the
    /// old scores read as missing, which is the symptom that was already there rather than a new one.
    /// </para>
    /// </summary>
    internal static unsafe class ResultKeys
    {
        /// <summary>Records moved onto this run's ids — read by the probes.</summary>
        internal static long Refiled;

        private const string Records = "GameResultsV4.highscores";
        private const string KeyCollection = "Dictionary.KeyCollection";
        private const string ValueCollection = "Dictionary.ValueCollection";

        /// <summary>
        /// Rewrites the stored keys of `table`'s highscore dictionary for this run's ids — see the
        /// class remarks. Called once, after the table is in hand and has answered its self-test.
        /// </summary>
        internal static void Migrate(IntPtr table)
        {
            if (table == IntPtr.Zero) return;

            try
            {
                IntPtr tableClass = FieldResolver.ClassPointer("GameResultsV4");
                int highscores = FieldResolver.Field("GameResultsV4", "highscores");
                if (tableClass == IntPtr.Zero || highscores < 0) { MigrateFailed("no table"); return; }

                IntPtr scores = Memory.Ptr(table + highscores);
                if (!Memory.LooksLikeObject(scores)) return;      // no dictionary, no keys

                // Each of this run's songs by name — the one thing about a song an update does not
                // move. `ById` holds exactly this run's imports, so a name in it is ours.
                var slots = new Dictionary<string, ushort>();
                foreach (KeyValuePair<ushort, ChartInfo> song in SongCatalog.ById)
                    slots[song.Value.BaseName] = song.Key;
                if (slots.Count == 0) return;                     // no songs this run: no key is ours

                // The dictionary's own methods, asked on the instantiated class, and the two element
                // types from its own signatures — see `Method` and `ElementClass`.
                IntPtr scoresClass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(scores);
                IntPtr count = Method(scoresClass, "get_Count", 0, Records);
                IntPtr keys = Method(scoresClass, "get_Keys", 0, Records);
                IntPtr values = Method(scoresClass, "get_Values", 0, Records);
                IntPtr take = Method(scoresClass, "Remove", 2, Records);       // Remove(key, out value)
                IntPtr write = Method(scoresClass, "set_Item", 2, Records);
                if (count == IntPtr.Zero || keys == IntPtr.Zero || values == IntPtr.Zero ||
                    take == IntPtr.Zero || write == IntPtr.Zero)
                { MigrateFailed("no method"); return; }

                IntPtr raised = IntPtr.Zero;
                IntPtr boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(count, scores, null, ref raised);
                int stored = Memory.LooksLikeObject(boxed) ? Memory.I32(boxed + Offsets.Runtime.BoxedData) : 0;
                if (stored <= 0) return;                          // nothing stored: nothing to move

                IntPtr collection = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(keys, scores, null, ref raised);
                IntPtr copy = Memory.LooksLikeObject(collection)
                    ? Method(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(collection), "CopyTo", 2, KeyCollection)
                    : IntPtr.Zero;
                IntPtr keyElement = ElementClass(copy);
                if (keyElement == IntPtr.Zero) { MigrateFailed("no key type"); return; }

                IntPtr records = Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(values, scores, null, ref raised);
                IntPtr valueCopy = Memory.LooksLikeObject(records)
                    ? Method(Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(records), "CopyTo", 2, ValueCollection)
                    : IntPtr.Zero;
                IntPtr recordElement = ElementClass(valueCopy);
                if (recordElement == IntPtr.Zero) { MigrateFailed("no record"); return; }

                // One slot per stored key, and one record to carry each of them over.
                IntPtr array = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(keyElement, (ulong)stored);
                IntPtr record = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(recordElement, 1);
                int stride = FieldResolver.ElementSize(array, "GameResultKey.Size");
                int nameAt = FieldResolver.Field("GameResultKey", "BaseName");
                int idAt = FieldResolver.Field("GameResultKey", "SongId");
                int songIdAt = FieldResolver.Field("GameResultV4", "SongId");
                if (!Memory.LooksLikeObject(array) || !Memory.LooksLikeObject(record) ||
                    stride <= 0 || nameAt < 0 || idAt < 0 || songIdAt < 0)
                { MigrateFailed("no shape"); return; }

                // The game's own reader fills the array, so the keys arrive whole and at this
                // build's layout.
                IntPtr* copyArgs = stackalloc IntPtr[2];
                int zero = 0;
                copyArgs[0] = array;
                copyArgs[1] = (IntPtr)(&zero);
                Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(copy, collection, (void**)copyArgs, ref raised);

                // Both arrays are this mod's, and only a raw address points at each — the game's
                // collector cannot see one. A handle says "this is a root"; see `CustomResults.Root`.
                // They are not freed again: this runs once a session, and a handle is a few bytes.
                Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(array, false);
                Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new(record, false);

                IntPtr keysAt = array + Offsets.Runtime.ArrayDataOffset;
                IntPtr recordAt = record + Offsets.Runtime.ArrayDataOffset;
                byte* next = stackalloc byte[stride];
                IntPtr* takeArgs = stackalloc IntPtr[2];
                IntPtr* writeArgs = stackalloc IntPtr[2];

                for (int i = 0; i < stored; i++)
                {
                    IntPtr key = keysAt + i * stride;
                    IntPtr namePointer = Memory.Ptr(key + nameAt);
                    string name = namePointer == IntPtr.Zero ? null : Memory.Text(namePointer);
                    if (name == null || !slots.TryGetValue(name, out ushort slot)) continue;
                    if (Memory.U16(key + idAt) == slot) continue;  // already this run's id

                    // Off the old key — the value comes with it — and onto this run's id.
                    takeArgs[0] = key;
                    takeArgs[1] = recordAt;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(take, scores, (void**)takeArgs, ref raised);

                    Memory.WriteU16(recordAt + songIdAt, slot);
                    Buffer.MemoryCopy((void*)key, next, stride, stride);
                    Memory.WriteU16((IntPtr)next + idAt, slot);

                    writeArgs[0] = (IntPtr)next;
                    writeArgs[1] = recordAt;
                    Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(write, scores, (void**)writeArgs, ref raised);

                    Refiled++;
                }
            }
            catch (Exception e)
            {
                MigrateFailed(Diagnostics.Describe(e));
            }
        }

        /// <summary>
        /// Says the pass could not be made: the short reason goes to the debug log, the shipping
        /// line states the cost. The name that did not answer was reported where it was asked —
        /// `FieldResolver` and <see cref="Method"/> both do that in every build.
        /// </summary>
        private static void MigrateFailed(string why)
        {
            Diagnostics.Warn("the stored song ids could not be re-filed: " + why);
            Diagnostics.Error("the custom results' stored song ids could not be rewritten, so scores " +
                              "recorded under an earlier build's ids will not be found: " + why);
        }

        /// <summary>
        /// The element class of a `CopyTo(array, index)`'s array parameter — or zero, which stops
        /// the pass.
        ///
        /// The type comes off the method the game itself declares, so it is *this* dictionary's TKey
        /// (or TValue) and nothing else's: the interop's own index is keyed by simple name and can
        /// answer with another type that shares it. Two steps from there to an array are properties
        /// of the runtime rather than choices. `il2cpp_method_get_param(CopyTo, 0)` is the array —
        /// the parameter list holds the declared parameters, and the `int` is at 1.
        /// `il2cpp_array_new` takes the **element** class, which it makes the array class from
        /// itself — an array type handed to it would become an array of arrays. Wrong either way is
        /// silent: an array of pointers is built, and the key's 16 bytes and the record's 96 are
        /// written through it all the same. The element must be a value type (`System.ValueType`'s
        /// child, the test <see cref="FieldResolver"/> makes too); anything else is refused the way
        /// a name that does not answer is.
        /// </summary>
        private static IntPtr ElementClass(IntPtr copy)
        {
            if (copy == IntPtr.Zero) return IntPtr.Zero;

            IntPtr type = Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param(copy, 0);
            IntPtr arrayClass = type == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_from_type(type);
            if (arrayClass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(arrayClass);
            if (element == IntPtr.Zero) return IntPtr.Zero;

            IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(element);
            return parent != IntPtr.Zero &&
                   Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(parent) == "ValueType"
                ? element
                : IntPtr.Zero;
        }

        /// <summary>
        /// One of the dictionary's methods, by name and parameter count, asked on the
        /// <b>instantiated</b> class — the count is what tells its two `Remove`s apart — or zero,
        /// reported by name, which stops the pass.
        /// </summary>
        private static IntPtr Method(IntPtr klass, string name, int parameters, string on)
        {
            IntPtr method = klass == IntPtr.Zero
                ? IntPtr.Zero
                : Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_method_from_name(klass, name, parameters);

            if (method == IntPtr.Zero)
                Diagnostics.Error($"{on}.{name}: no method by that name in this build");

            return method;
        }
    }
}
