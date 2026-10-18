using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// `SongSelectScene._MN(SongId, ChartDifficultyFlag, _VA) -> bool` — apply a selection.
        ///
        /// The song select's one funnel: every path that changes what is selected — a card click, the
        /// left/right arrows, a difficulty button, the scene's own first draw — ends up here with the
        /// song and the difficulty it wants shown. That makes it the only place where the two are
        /// still known <i>at the moment they are applied</i>, which is what the pictures need.
        /// </summary>
        // Prefixed because `Hooks` is a partial class and the jacket's file already has an `Image`
        // and a `Namespace` — a different image, so the names have to say which is which.
        private const string SceneImage = "Game.dll";

        private const string SceneNamespace = "ifapp.Game.Scenes";

        /// <summary>Selections applied. Counted rather than logged: this runs on every repaint.</summary>
        internal static long ApplyCalls;

        /// <summary>
        /// Times the difficulty changed without the song changing, and the picture was reloaded
        /// anyway. The count that says the fix is doing something.
        /// </summary>
        internal static long ApplyReloaded;

        /// <summary>
        /// `SongSelectScene._MN(SongId songId, ChartDifficultyFlag difficulty, _VA transition)`.
        ///
        /// The third parameter is the transition the caller wants: it reaches
        /// `LargeSongCard._OS` unchanged, where 0 swaps the material with no animation and 1 and 2
        /// run one of the two slide tweens. It is left as an `int` and forwarded untouched — it is
        /// the tempting lever, because the same gate that decides whether the pictures are reloaded
        /// also tests it, and pulling it would slide the card in on every difficulty change. See
        /// <see cref="MnDetour"/> for what is done instead.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte ApplyFn(IntPtr self, ushort songId, byte difficulty, int transition,
                                      IntPtr methodInfo);

        private static NativeHook<ApplyFn> _apply;
        private static ApplyFn _applyTramp;

        private static bool InstallSongSelect()
        {
            // By runtime walk, like `_sN` on the same class: the store route answers zero for some of
            // this class's methods, and a zero there falls back to a pinned address silently.
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "_MN", SceneImage, SceneNamespace);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("SongSelectScene._MN could not be resolved; a song with a picture per " +
                                 "difficulty will keep showing the one it was first drawn with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _apply = new NativeHook<ApplyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, ushort, byte, int, IntPtr, byte>)&MnDetour,
            };
            _apply.Attach();
            _applyTramp = _apply.Trampoline;
            Diagnostics.Info("SongSelectScene._MN hooked");
            return Landed(Hook.SongSelectApply, target, prologue);
        }

        private static void DetachSongSelect()
        {
            _apply?.Detach();
            _apply = null;
            _applyTramp = null;
        }

        /// <summary>
        /// Records the selection the game is about to apply, and — when only the difficulty changed —
        /// leaves the selected song reading as changed for the length of the call.
        ///
        /// <para>
        /// Two problems, one call. The first is that the pictures this leads to are asked for by
        /// <b>difficulty</b>, and the two places this mod could read one — the payload the play
        /// handler writes, and the scene's own difficulty field — are either not written yet or not
        /// reachable yet at that moment. This detour is handed the difficulty, so it takes it from
        /// there; see <see cref="Selection"/>.
        /// </para>
        /// <para>
        /// The second is that the game reloads the selection's pictures only when the <b>song</b>
        /// changed — a difficulty change does not reload them, so the song-select background keeps the
        /// picture of the difficulty the song was first drawn at. What follows fixes that from the
        /// game's own side rather than by reimplementing any part of it: the gate is a comparison
        /// against the selected-song field, so that field is set to a value no song can have for the
        /// duration of the call. The game then takes its reload path with the arguments it was
        /// already given, and writes the field back itself.
        /// </para>
        /// <para>
        /// What is deliberately <i>not</i> touched is the fourth argument. It looks like the obvious
        /// lever — the same gate tests it — but it also selects a transition animation on the large
        /// card, so forcing it would make every difficulty change slide the card in. Nothing else in
        /// the body reads either, which is why the shadowed field gives the reload alone.
        /// </para>
        /// </summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte MnDetour(IntPtr self, ushort songId, byte difficulty, int transition,
                                     IntPtr methodInfo)
        {
            ApplyCalls++;

            bool shadowed = false;
            if (!Faulted)
            {
                try
                {
                    // Before the game's body runs, because the jacket request this leads to happens
                    // inside it and has to see the new answer. The answer to "is this a difficulty
                    // the last call did not have" comes back from the same call: it is the field
                    // the recording writes, and reading it afterwards compares it with itself.
                    bool changed = Selection.Applying(self, songId, difficulty);

                    // The illustrator is the one text the game shows per song while the `if` file can
                    // give it per difficulty, so it follows the difficulty being applied. Here rather
                    // than in `Applying` because it is not part of "which selection is in force" —
                    // and it is written only when the value would change, which this call site cannot
                    // know, so the deciding is inside. See `SongCatalog.ShowIllustrator`.
                    SongCatalog.ShowIllustrator(songId, difficulty);

                    shadowed = Selection.ShadowUnlessSongChanged(self, songId, changed);
                    if (shadowed) ApplyReloaded++;
                }
                catch (Exception e)
                {
                    Fault(Hook.SongSelectApply, e);
                }
            }

            byte applied = _applyTramp(self, songId, difficulty, transition, methodInfo);

            // Outside the try, like every other detour here: the game's call is made whatever
            // happened above. The restore is after it because the field has to stay shadowed for the
            // whole call.
            if (shadowed) Selection.Unshadow(self, songId);

            return applied;
        }

        /// <summary>The namespace `_NH` — the player's save — is resolved in: the scenes' parent.</summary>
        private const string SaveNamespace = "ifapp.Game";

        /// <summary>
        /// `_K._NH._UoA(in SongInfo, ChartDifficultyFlag, byte flags) -> byte` — the state of one
        /// difficulty of one song.
        ///
        /// <para>
        /// It is the game's one answer to "what is this difficulty": <b>0</b> when the song has no
        /// chart for it, <b>1</b> when it is locked — the story it belongs to has not been reached,
        /// the pack it sits in is unowned DLC, or its rating is above what the save has unlocked —
        /// and <b>2</b> when it is open. Every consumer asks here: the play gate (`_sN`, which starts
        /// a song only on a 2), the difficulty buttons and the refresh that redraws them, the song
        /// cards, the results screen, and the panel that explains an unlock.
        /// </para>
        /// <para>
        /// For a song this mod imported the answer is this mod's own. <b>Every difficulty is open</b>:
        /// a chart that came off the disk is not the save's to unlock, and leaving it to the game's
        /// gates is what made the upper difficulties of a custom song follow a fresh save's story
        /// progress. A difficulty the `if` file left chartless answers <b>0</b> — the game's own "this
        /// song has no chart here", the same thing it says about a shipped song that lacks one.
        /// Nothing is ever answered 1: that answer is the one the game dresses up with its unlock
        /// panel and its progress, and a chart that is not there is not a lock to be explained.
        /// </para>
        /// <para>
        /// The song is read the way the function being stood in front of reads it: a record's first
        /// field is its `SongId` — that is what `_UoA` indexes `allSongInfo` with, checking the
        /// element's own id against the index before it uses it. Everything else is passed through
        /// untouched.
        /// </para>
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte DifficultyStateFn(IntPtr save, IntPtr access, IntPtr songInfo,
                                                byte difficulty, byte flags, IntPtr methodInfo);

        private static NativeHook<DifficultyStateFn> _state;
        private static DifficultyStateFn _stateTramp;

        /// <summary>Difficulty questions asked, and how many of them were about this mod's songs.</summary>
        internal static long DifficultyAsked, DifficultyOurs;

        private static bool InstallDifficultyState()
        {
            // By runtime walk, like the two other `_NH` methods this mod reaches: the walk answers for
            // this class where the generated store does not.
            IntPtr target = MethodResolver.ByRuntime("_NH", "_UoA", SceneImage, SaveNamespace);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Error("_NH._UoA: no method by that name in this build; a custom song's " +
                                  "difficulties will follow the game's own locks, and a fresh save may " +
                                  "leave them locked");
                return false;
            }

            byte[] prologue = Prologue(target);
            _state = new NativeHook<DifficultyStateFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, byte, byte, IntPtr, byte>)&DifficultyDetour,
            };
            _state.Attach();
            _stateTramp = _state.Trampoline;
            Diagnostics.Info("_NH._UoA hooked");
            return Landed(Hook.DifficultyState, target, prologue);
        }

        private static void DetachDifficultyState()
        {
            _state?.Detach();
            _state = null;
            _stateTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static byte DifficultyDetour(IntPtr save, IntPtr access, IntPtr songInfo, byte difficulty,
                                             byte flags, IntPtr methodInfo)
        {
            DifficultyAsked++;

            if (!Faulted)
            {
                try
                {
                    if (Ours(songInfo, difficulty, out ChartInfo info, out int index))
                    {
                        DifficultyOurs++;

                        // A chart that is not there, or a difficulty that is open. See the summary.
                        return info.NoChart[index] ? (byte)0 : (byte)2;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.DifficultyState, e);
                }
            }

            return _stateTramp(save, access, songInfo, difficulty, flags, methodInfo);
        }

        /// <summary>
        /// Whether the song asked about is one of this mod's, and which difficulty the flag names.
        ///
        /// The id is read at the offset the field resolver answers for `SongInfo.Id`. The record is a
        /// value type, so that offset is within the record itself — the copies the game hands this
        /// call and the heap records alike — and the same two reads every other "is this ours" test in
        /// this mod makes are the ones here.
        /// </summary>
        private static bool Ours(IntPtr songInfo, byte difficulty, out ChartInfo info, out int index)
        {
            info = null;
            index = -1;

            if (!Offsets.Ready || !Memory.LooksLikeObject(songInfo)) return false;
            if (!SongCatalog.ById.TryGetValue(Memory.U16(songInfo + Offsets.Song.Id), out info)) return false;

            index = SongCatalog.DifficultyIndexOf(difficulty);
            return index >= 0;
        }
    }
}
