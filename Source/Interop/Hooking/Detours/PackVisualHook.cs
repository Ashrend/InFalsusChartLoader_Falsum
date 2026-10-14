using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// `PackVisualMemberLarge._vc(ChartDifficultyFlag) -> void` — the pack screen's difficulty apply.
        ///
        /// <para>
        /// The category screen draws one small jacket per song of the selected pack, on
        /// `PackSongCardMember` cards, and it asks for each of them exactly once — when a pack is
        /// selected and the cards are built (`PackVisualMemberLarge._Sc`, which carries its own inlined
        /// copy of the card build). A difficulty change does not ask again: it runs this method, which
        /// walks the cards and updates their texts, their difficulty-based objects and their clear-type
        /// material, and leaves every jacket as it was. That is measured, not inferred — the body calls
        /// `_qc` once per card and never `_apA` or `_MIA`.
        /// </para>
        /// <para>
        /// So a song with a picture per difficulty keeps showing the one its card was built with, and on
        /// a fresh session it shows nothing at all: this screen has no source of its own for the
        /// difficulty, so `Selection` answers with the song-select's last value or with nothing. Both are
        /// answered from the argument already in this call:
        /// </para>
        /// <list type="number">
        /// <item><description>
        /// The difficulty is recorded <b>before</b> the game's body runs. On the entry path the apply
        /// comes first and the cards are built after it, so the jackets asked for during that build are
        /// answered with the difficulty actually in force rather than with the previous screen's.
        /// </description></item>
        /// <item><description>
        /// When the cards on screen were built for a different difficulty, they are rebuilt after the
        /// game's body runs — by calling the game's own card builder, `PackSongCardMember._pc`, once per
        /// card that is one of this mod's. That is the sequence `_Sc` performs inline when a pack is
        /// chosen, so it is the game's own code that re-asks `_apA` and substitutes through `_MIA`, and
        /// the picture follows the difficulty without a scene reload.
        /// </description></item>
        /// </list>
        /// <para>
        /// Nothing here is pinned: both methods are resolved by name, so a miss leaves the hook
        /// uninstalled rather than patching a wrong address — see <see cref="MethodResolver"/> and
        /// the note on `_MIA`.
        /// </para>
        /// </summary>
        // (No RVA is kept here: `PackVisualMemberLarge._vc` and `PackSongCardMember._pc` are both
        // resolved by name — see MethodResolver.ByName.)

        /// <summary>
        /// `PackSongCardMember._pc(DataAccess, _SH) -> void` — build one card and its jacket.
        ///
        /// <b>Unreferenced in the installed build.</b> `_Sc` carries its own inlined copy of this body;
        /// the method itself has no code caller left (its only xrefs are the metadata tables). It is
        /// still the game's own card build, byte for byte the sequence `_Sc` performs — `_apA` for the
        /// reference, `_MIA` for the material with `forceReload`, the material onto the card, and `_qc`
        /// to re-apply the card's own difficulty — and calling it is one call instead of a copy of that
        /// sequence here. Nothing is invented: the alternative would be this mod re-implementing four
        /// of the game's steps and staying in step with them afterwards.
        ///
        /// Reached by name only, for the reason above: on a build where it is gone this degrades to
        /// "the picture updates when the screen is rebuilt", which is what happens now anyway.
        /// </summary>

        // Prefixed because `Hooks` is a partial class and other files already name this image and
        // namespace under their own class's terms.
        private const string PackImage = "Game.dll";

        private const string PackNamespace = "ifapp.Game";

        // No offsets are written in this file: every one is asked by name (`ResolveFields`), and each
        // stays at `Offsets.Unresolved` (-1) until it answers. See `Offsets` for why.

        /// <summary>`PackVisualMemberLarge._Mf`, the cards of the pack on screen.</summary>
        private static int CardsField = Offsets.Unresolved;

        /// <summary>`PackSongCardMember._if`, the song view model the card was built from.</summary>
        private static int CardViewModel = Offsets.Unresolved;

        /// <summary>
        /// `PackVisualMemberLarge.dataAccess`, which the card's build has to be handed.
        ///
        /// It is the pack visual's own field rather than anything on the card: `_Sc` passes this one
        /// to the card build it performs inline, and the card's build only forwards it to `_qc`, which
        /// reads the song data and the localisation table out of it.
        /// </summary>
        private static int PackDataAccess = Offsets.Unresolved;

        /// <summary>`_SH._WEb`, the song record — embedded, so this is its address and not a pointer.</summary>
        private static int ViewModelSong = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.PackSelectLargeNonCompleted` — the large-card material slot.</summary>
        private static int RowLargeCard = Offsets.Unresolved;

        /// <summary>`SongSelectPackAssets.PackSelectNonCompleted` — the small-card material slot.</summary>
        private static int RowSmallCard = Offsets.Unresolved;

        private static bool _fieldsResolved;

        private static bool _fieldsMissing;

        /// <summary>
        /// The five offsets this file reads, asked of the running game by name, once, and whether all
        /// of them answered.
        ///
        /// They are fields of *this build of the game*, not of this mod: a patch moves them and the
        /// mod reads a plausible-looking value from the wrong place rather than failing. A name that
        /// cannot be resolved is reported by <see cref="FieldResolver.Field"/> and answered false
        /// here — the cards are then left exactly as the game built them, which is the behaviour this
        /// hook exists to improve on, and the run says why rather than comparing or rebuilding
        /// through an offset that is not there.
        /// </summary>
        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            PackDataAccess = FieldResolver.Field("PackVisualMemberLarge", "dataAccess");
            ViewModelSong = FieldResolver.Field("_SH", "_WEb");
            CardsField = FieldResolver.Field("PackVisualMemberLarge", "_Mf");
            CardViewModel = FieldResolver.Field("PackSongCardMember", "_if");
            ViewModelDifficulty = FieldResolver.Field("_SH", "_xEb");
            RowLargeCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectLargeNonCompleted");
            RowSmallCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectNonCompleted");

            if (PackDataAccess < 0 || ViewModelSong < 0 || CardsField < 0 ||
                CardViewModel < 0 || ViewModelDifficulty < 0 ||
                RowLargeCard < 0 || RowSmallCard < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the pack's card fields could not be read by name in this build; the " +
                                  "cards will keep the difficulty they were first built with");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        /// <summary>`_SH._xEb`, the difficulty the card is showing.</summary>
        private static int ViewModelDifficulty = Offsets.Unresolved;

        /// <summary>
        /// `void _vc(ChartDifficultyFlag)`.
        ///
        /// The difficulty is a one-byte enum in this build (`_SH._xEb`, `PackSelectScene._Gf` and this
        /// method's own parameter all read as bytes), which is the same shape the song-select's `_MN`
        /// is hooked with.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackDifficultyFn(IntPtr self, byte difficulty, IntPtr methodInfo);

        private static NativeHook<PackDifficultyFn> _packDifficulty;
        private static PackDifficultyFn _packDifficultyTramp;

        /// <summary>`PackSongCardMember._pc`'s code pointer, or null when it could not be reached.</summary>
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> _buildCard;

        /// <summary>
        /// Its `MethodInfo`, which the call passes as its last argument the way a call site does.
        /// A method's body can reach for it, and this mod is standing in for a call site.
        /// </summary>
        private static IntPtr _buildCardInfo;

        /// <summary>Difficulty applies seen on the pack screen, and cards rebuilt because of one.</summary>
        internal static long PackDifficultyCalls, PackCardsRebuilt;

        // ------------------------------------------------------------------------------------------
        // The pack's picture: `packToAssets[PackInfo.Id]`, read through one shared accessor.
        // ------------------------------------------------------------------------------------------

        /// <summary>
        /// `void accessor(SongSelectPackAssets[] table, SongSelectPackAssets* destination, uint index)`
        /// — IL2CPP's shared element accessor for this array type, which copies one 216-byte row out.
        ///
        /// <b>Every</b> read of a pack's row goes through it — thirteen call sites, every member of the
        /// pack screen (`PackVisualMemberLarge._Ef` and its siblings, `PackVisualMemberSmall._bC`,
        /// `PackOutlineMember`, `PackDLCVisualMemberSingle`, `DLCNotificationContainer`,
        /// `PackSelectButtonContainer.Update`). A row is a pack's whole look: the large card, the small
        /// card, the backing image, the outline.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackRowFn(IntPtr table, IntPtr destination, uint index);

        private static NativeHook<PackRowFn> _packRow;
        private static PackRowFn _packRowTramp;

        /// <summary>The row this mod's pack is answered with; -1 until it has been resolved.</summary>
        private static int _rowSource = -1;

        private static bool InstallPackRow()
        {
            int candidates = 0;
            IntPtr target = RowAccessor(ref candidates);
            if (target == IntPtr.Zero)
            {
                if (candidates < 0)
                {
                    // The getter is the missing piece, and it is reached by name: that is a different
                    // failure from "the helper it calls could not be told apart", so it says so.
                    Diagnostics.Error("the game's pack row getter could not be found by name in this " +
                                      "build; the custom pack will wear whatever this build puts in its row");
                    return false;
                }

                Diagnostics.Info($"the pack row accessor: the getter's most-called function is called " +
                                 $"{candidates} time(s); a row is {RowSize()} byte(s)");
                Diagnostics.Error("the pack row accessor could not be located in this build; the custom " +
                                  "pack will wear whatever this build puts in its row");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packRow = new NativeHook<PackRowFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, void>)&PackRowDetour,
            };
            _packRow.Attach();
            _packRowTramp = _packRow.Trampoline;
            Diagnostics.Info($"the pack row accessor is at 0x{target.ToInt64():X}");
            return Landed(Hook.PackRow, target, prologue);
        }

        /// <summary>
        /// Answers the read for this mod's pack with the In Falsus pack's row.
        ///
        /// This is the whole of how the custom pack gets its look, and it is deliberately a
        /// substitution at the read rather than a row this mod keeps writing: a pack's picture is
        /// whatever this accessor returns for its id, so the mod's pack is given the row of the pack it
        /// copies — the In Falsus one — at the moment the picture is fetched.
        ///
        /// Keeping a row of its own was tried and does not work in this build, measured from the running
        /// game: the DLC layer creates its own `PackSelectSceneAssets` instances at runtime, hands one to
        /// the pack screen's members, and rewrites rows 0, 1 and 7 of that copy with the unowned/DLC art —
        /// including the row of a pack it has no record of (this mod's). A row kept in one table is
        /// therefore not the row the screen reads, and keeping "the In Falsus row" was worse than
        /// useless: in the copy the screen reads, that row had itself been rewritten, so the pack wore
        /// the unowned look faithfully and forever.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackRowDetour(IntPtr table, IntPtr destination, uint index)
        {
#if DEBUG
            RowCalls++;
            long t0 = Now;
#endif
            if (SongCatalog.CustomPackId >= 0 && index == (uint)SongCatalog.CustomPackId)
            {
                if (_rowSource < 0) _rowSource = ResolveSourceRow();
                if (_rowSource >= 0)
                {
                    index = (uint)_rowSource;
#if DEBUG
                    RowRedirects++;
#endif
                }
            }
#if DEBUG
            TicksRow += Now - t0;
#endif

            _packRowTramp(table, destination, index);
        }

        /// <summary>
        /// The row this mod's pack is answered with — the same row it copied its look from when the
        /// table was grown. Asked of the table the mod grew, not written down: which pack that is
        /// follows the game's own pack list, and a number typed here would be right only until the
        /// next update moves it (the lesson the hardcoded pack id 7 taught).
        /// </summary>
        private static int ResolveSourceRow()
        {
            // A "not yet" is silent: before registration there is no table to read, and the question
            // is re-asked on the next call. Everything past this gate has a table in hand, so a -1
            // is a real answer — and one the player would see as the wrong picture on the custom
            // pack — which is why it is said once (`PackSetup.NoRow`) rather than retried forever
            // in silence, which is the shape this fix exists to remove.
            if (!PackSetup.TryGetVisuals(out IntPtr canonical) || !Memory.LooksLikeObject(canonical))
                return -1;

            int offset = PackSetup.VisualsTableOffset;
            IntPtr table = offset > 0 ? Memory.Ptr(canonical + offset) : IntPtr.Zero;
            int count = Memory.LooksLikeObject(table) ? Memory.I32(table + Offsets.Runtime.ArrayLength) : 0;
            int stride = Stride(table, count);
            int row = stride <= 0 ? -1 : SourceRow(table, count, stride);
            if (row < 0) PackSetup.NoRow();
            return row;
        }

        /// <summary>
        /// The helper the game's own row getter calls — asked of the game, not searched for by shape.
        ///
        /// That helper has no name to resolve by: it is not a method the game declares — measured, its
        /// address is in no method-pointer table and no name table carries it — but a body the compiler
        /// emitted for this one array type, called from thirteen places. So it is reached through one of
        /// its callers, and that caller <i>is</i> named: `PackSelectSceneAssets._WnA`, the game's own
        /// "the row for this pack id" getter, the one method whose whole job is to copy a row out.
        ///
        /// It copies a row for the id it was handed and another for the fallback row it uses when that
        /// id is out of range, so the helper is the function it calls <b>more than once</b>. That is the
        /// whole of the search: walk the getter's first bytes, take every `call` — `E8` and a
        /// displacement, which is x86 and not this game — and answer with the target called from two or
        /// more sites, as long as exactly one target is called that often.
        ///
        /// Nothing of the game's code is written down: an encoding is the architecture's, the window is
        /// a bound this mod chooses, and where a call lands is read off the call. The row size that used
        /// to be baked into a shape this search matched is not needed here at all — <see cref="RowSize"/>
        /// survives only to say, in a log, what a row turned out to be.
        ///
        /// Zero when the getter cannot be resolved, when nothing inside the window is called twice, or
        /// when more than one thing is; <paramref name="candidates"/> carries the count seen, and -1
        /// means the getter itself was the missing piece.
        /// </summary>
        private static IntPtr RowAccessor(ref int candidates)
        {
            IntPtr getter = MethodResolver.ByName("PackSelectSceneAssets", "_WnA");
            if (getter == IntPtr.Zero)
            {
                candidates = -1;
                return IntPtr.Zero;
            }

            byte* code = (byte*)getter;
            IntPtr best = IntPtr.Zero;
            int bestCount = 0;
            bool tied = false;

            for (int at = 0; at + CallLength <= CallWindow; at++)
            {
                if (code[at] != 0xE8) continue;                                  // `call rel32`

                IntPtr target = Target(code + at);
                if (!Executable(target)) continue;

                int count = 1;
                for (int other = at + CallLength; other + CallLength <= CallWindow; other++)
                    if (code[other] == 0xE8 && Target(code + other) == target)
                        count++;

                if (count > bestCount) { bestCount = count; best = target; tied = false; }
                else if (count == bestCount) tied = true;
            }

            candidates = bestCount;
            return bestCount > 1 && !tied ? best : IntPtr.Zero;
        }

        /// <summary>`call rel32` — one opcode byte and four of displacement.</summary>
        private const int CallLength = 5;

        /// <summary>
        /// How far into the getter calls are looked for. A bound this mod chooses, not a measurement:
        /// the getter is small — a bounds check and two copies — and a window that reached past it into
        /// the next function could only add calls that are made once.
        /// </summary>
        private const int CallWindow = 96;

        /// <summary>Where an `E8` at <paramref name="call"/> calls, read off the instruction.</summary>
        private static IntPtr Target(byte* call)
        {
            return (IntPtr)((long)(call + CallLength) + *(int*)(call + 1));
        }

        /// <summary>
        /// Whether an address is inside one of the module's executable sections — asked of the module's
        /// own PE headers, so no address and no section layout is written down.
        /// </summary>
        private static bool Executable(IntPtr address)
        {
            byte* image = (byte*)GameAssembly.Base;
            if (image == null || *(ushort*)image != 0x5A4D) return false;        // "MZ"

            byte* pe = image + *(int*)(image + 0x3C);
            if (*(uint*)pe != 0x00004550) return false;                         // "PE\0\0"

            int sections = *(ushort*)(pe + 6);
            byte* section = pe + 24 + *(ushort*)(pe + 20);
            long at = address.ToInt64();

            for (int s = 0; s < sections; s++, section += 40)
            {
                // Only what the loader itself marked executable.
                if ((*(uint*)(section + 36) & 0x20000000) == 0) continue;

                uint size = *(uint*)(section + 8);
                uint start = *(uint*)(section + 12);
                if (size == 0 || start == 0) continue;

                long from = (long)(image + start);
                if (at >= from && at < from + size) return true;
            }

            return false;
        }

        /// <summary>
        /// One `SongSelectPackAssets` in bytes — asked of the game, never written down. See
        /// <see cref="FieldResolver.ValueSize"/>, which is the same question this hook's own fields are
        /// answered by: the array's element type, less the object header a boxed one carries.
        /// Measured on this build: 216.
        ///
        /// Zero when it cannot be had, and the caller refuses on zero: a search built from a size that
        /// is not this game's is a search for an address this game does not have. Not remembered on
        /// failure — a class the index has not been asked for yet is not a class that does not exist.
        /// </summary>
        private static int RowSize()
        {
            if (_rowSize >= 0) return _rowSize;

            int size = FieldResolver.ValueSize("SongSelectPackAssets");
            if (size <= 0) return 0;

            return _rowSize = size;
        }

        /// <summary>`RowSize`'s answer, -1 until it has been asked.</summary>
        private static int _rowSize = -1;

        /// <summary>
        /// Which shipped row a new pack copies its look from: <b>the first one the game draws</b> — the
        /// entry after the reserved one at the head of the list, which is the In Falsus pack — or, if
        /// that one has nothing to draw with, the first row after it that has. -1 when none does.
        ///
        /// The row is found, not named: which pack Is In Falsus follows the game's pack list, so it is
        /// read there rather than written here.
        /// </summary>
        internal static int SourceRow(IntPtr table, int count, int stride)
        {
            if (stride <= 0 || count <= 0) return -1;

            for (int i = 1; i < count; i++)
                if (i != SongCatalog.CustomPackId && Drawable(table, count, stride, i)) return i;

            return -1;
        }

        /// <summary>A row's size in bytes, measured off the array rather than assumed.</summary>
        private static int Stride(IntPtr table, int count)
        {
            if (!Memory.LooksLikeObject(table) || count <= 0) return 0;

            try
            {
                return (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table) / count);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Whether a row has a material in a slot the pack screen draws from — the large card
        /// (`PackSelectLargeNonCompleted`) or the small one (`PackSelectNonCompleted`). Any, not
        /// every: rows in this build differ in which slots they carry (measured: rows 0 and 1 have a
        /// first material and no backing or unowned one).
        /// </summary>
        private static bool Drawable(IntPtr table, int count, int stride, int id)
        {
            if (!ResolveFields()) return false;

            IntPtr row = Row(table, count, stride, id);
            if (row == IntPtr.Zero) return false;

            return Memory.LooksLikeObject(Memory.Ptr(row + RowLargeCard))
                || Memory.LooksLikeObject(Memory.Ptr(row + RowSmallCard));
        }

        /// <summary>The row an id selects — `packToAssets[id]`, and row zero for an id past the end.</summary>
        private static IntPtr Row(IntPtr table, int count, int stride, int id)
        {
            if (stride <= 0 || count <= 0) return IntPtr.Zero;
            if (id < 0 || id >= count) id = 0;

            return table + Offsets.Runtime.ArrayDataOffset + id * stride;
        }

        private static bool InstallPackVisual()
        {
            // Before `_vc`: the row accessor answers for this mod's pack from the first frame the screen
            // can draw, and `_vc` is what makes a difficulty change reach the cards afterwards.
            bool row = InstallPackRow();

            IntPtr info = MethodResolver.MethodInfoByRuntime("PackSongCardMember", "_pc", PackImage, PackNamespace);
            if (info == IntPtr.Zero)
            {
                // Not fatal, and not a hook: without it the difficulty is still recorded for the cards
                // the game builds later — it just does not reach the ones already on screen.
                Diagnostics.Warn("PackSongCardMember._pc could not be found; a difficulty change on the " +
                                 "pack screen will not reach the jackets until the screen is reloaded");
            }
            else
            {
                _buildCardInfo = info;
                _buildCard = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)(*(IntPtr*)info);
            }

            // By runtime walk, like the other methods on this screens' classes: the generated store
            // answers zero for methods nothing has touched, so the walk is what reaches them. A miss
            // leaves the hook uninstalled and says so; `Attach()` would report nothing either way, so
            // a wrong address here would be a patch into the middle of whatever now occupies it.
            IntPtr target = MethodResolver.ByRuntime("PackVisualMemberLarge", "_vc", PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("PackVisualMemberLarge._vc could not be found; the jackets on the pack " +
                                 "screen will keep the difficulty they were first built with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packDifficulty = new NativeHook<PackDifficultyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)&PackDifficultyDetour,
            };
            _packDifficulty.Attach();
            _packDifficultyTramp = _packDifficulty.Trampoline;
            Diagnostics.Info("PackVisualMemberLarge._vc hooked");
            return Landed(Hook.PackVisual, target, prologue) & row;
        }

        private static void DetachPackVisual()
        {
            _packRow?.Detach();
            _packRow = null;
            _packRowTramp = null;

            _packDifficulty?.Detach();
            _packDifficulty = null;
            _packDifficultyTramp = null;
            _buildCard = null;
            _buildCardInfo = IntPtr.Zero;
        }

        /// <summary>
        /// Records the difficulty being applied, and rebuilds the cards after the game's own body has
        /// moved them if they were showing another one.
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackDifficultyDetour(IntPtr self, byte difficulty, IntPtr methodInfo)
        {
            PackDifficultyCalls++;

            bool rebuild = false;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    // Before the body, and both questions have to be asked here: the body is what moves
                    // the cards' own difficulty, so afterwards "were they showing another one" can only
                    // answer no.
                    Selection.ApplyingDifficulty(difficulty);
                    rebuild = CardsForAnotherDifficulty(self, difficulty);
                }
                catch (Exception e)
                {
                    Fault(Hook.PackVisual, e);
                }
#if DEBUG
                TicksVc += Now - t0;
#endif
            }

            _packDifficultyTramp(self, difficulty, methodInfo);

            // Outside the try, like every other detour here: the game's call is made whatever happened
            // above. The rebuild is after it, because what it re-runs reads the state the body just set.
            if (rebuild)
            {
#if DEBUG
                long t1 = Now;
#endif
                RebuildCards(self);
#if DEBUG
                TicksVc += Now - t1;
#endif
            }
        }

        /// <summary>
        /// Whether a card on screen was built for a difficulty other than the one being applied.
        ///
        /// Only this mod's songs count. A shipped pack's cards carry one picture for the whole song, so
        /// rebuilding them would be an Addressables reload with nothing to show for it. An empty list is
        /// not stale either: on the entry path the difficulty is applied before the pack is chosen, and
        /// there is then nothing on screen to be wrong.
        /// </summary>
        private static bool CardsForAnotherDifficulty(IntPtr packVisual, byte difficulty)
        {
            // Asked for here, not further down. This is the first line in the file that reads one of
            // the game's fields, and the only other places that resolve them (`Ours`, `RebuildCards`)
            // are reached *after* the list has been read below — so without this call the very first
            // question ("were these cards built for another difficulty?") would be asked with the
            // constant this build was reversed with. A name that did not answer is reported by
            // `ResolveFields` and answered here as "nothing to do".
            if (!ResolveFields()) return false;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr _, out IntPtr viewModel)) continue;
                if (Memory.U8(viewModel + ViewModelDifficulty) != difficulty) return true;
            }

            return false;
        }

        /// <summary>
        /// Re-runs the game's own card build for each of this mod's cards.
        ///
        /// One call per card, and it is the game's sequence rather than a copy of it: the reference from
        /// `_apA`, the substitution through `_MIA` with `forceReload`, the material onto the card, and
        /// the card's own difficulty re-applied. A failure here does <b>not</b> fault: this is an extra
        /// the mod performs on the player's behalf, and it must not be able to switch off the chart and
        /// audio hooks — the lesson the run with the card-counting diagnostic was written from.
        /// </summary>
        private static void RebuildCards(IntPtr packVisual)
        {
            if (_buildCard == null) return;

            if (!ResolveFields()) return;

            IntPtr dataAccess = Memory.Ptr(packVisual + PackDataAccess);
            if (!Memory.LooksLikeObject(dataAccess)) return;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr card, out IntPtr viewModel)) continue;

                try
                {
                    _buildCard(card, dataAccess, viewModel, _buildCardInfo);
                    PackCardsRebuilt++;
                }
                catch (Exception e)
                {
                    Reported("rebuilding a card on the pack screen raised: " + Diagnostics.Describe(e));
                }
            }
        }

        /// <summary>One card of the list, when it is one of this mod's songs'.</summary>
        private static bool Ours(IntPtr items, int index, out IntPtr card, out IntPtr viewModel)
        {
            card = IntPtr.Zero;
            viewModel = IntPtr.Zero;

            // First, before either resolved field below is read. Both callers ask for them before
            // calling this today; this is so that the next caller cannot forget — an unresolved
            // offset is -1, and the read would be aimed one byte before the card.
            if (!ResolveFields()) return false;

            card = Memory.Ptr(items + index * IntPtr.Size);
            if (!Memory.LooksLikeObject(card)) return false;

            viewModel = Memory.Ptr(card + CardViewModel);
            if (!Memory.LooksLikeObject(viewModel)) return false;

            // The record is embedded in the view model, so its address is the view model plus the
            // field's offset — and the base name is inside it, which is what the catalogue matches on.
            return JacketCatalog.IsOurs(viewModel + ViewModelSong);
        }

        /// <summary>
        /// Says why the rebuild failed, once.
        ///
        /// <see cref="RebuildCards"/> runs on every difficulty change, and a card that cannot be
        /// rebuilt cannot be rebuilt on the next one either: a line per attempt would be its own defect
        /// on a path the player can drive. `[Conditional]` for the reason the log class gives — without
        /// it the argument's string is built in Release, where nothing reads it.
        /// </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void Reported(string why)
        {
            if (_rebuildReported) return;
            _rebuildReported = true;
            Diagnostics.Warn(why);
        }

        private static bool _rebuildReported;
    }
}
