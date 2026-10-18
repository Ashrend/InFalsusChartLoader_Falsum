using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MelonLoader.NativeUtils;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>
        /// `DifficultyUnlockContainer._GB(in _lH before, in _lH after)` — the results screen's
        /// "difficulty unlock progress" container.
        ///
        /// <para>
        /// The game fills that container from two save snapshots: the one taken before the result
        /// was merged and the one after. Its only caller is `ResultsScene._jD.MoveNext`, and what it
        /// shows is the save's own progression — `_lH._meb`, the percentage the settlement wrote —
        /// not the answer `_NH._UoA` gives for a song and a difficulty.
        /// </para>
        /// <para>
        /// A custom song's result is merged into this mod's own table, and every difficulty of a
        /// custom song is answered open by `_UoA`; the save's progression has nothing to say about
        /// whether that difficulty may be played. The container would nevertheless announce progress
        /// toward unlocking one — the game's own calculation, shown for a lock that does not exist
        /// for this song. So for a custom song the detour hands the method the same "after" snapshot
        /// with one field changed: the difficulty it reads first, set to the enum member the method
        /// treats as "none". That is the game's own hide branch — it deactivates the container and
        /// returns before the fill, the percentage and the tween are touched — so the bar is hidden
        /// and the progress on it is not refreshed.
        /// </para>
        /// <para>
        /// The song is the one being played, read the way the settlement reads it
        /// (<see cref="Selection.TryReadSong"/>) and tested the way the settlement tests it
        /// (<see cref="CustomResults.IsOurs"/>). No criterion of its own. The enum member, the
        /// field's offset and the record's size are all asked of the game by name; a miss leaves this
        /// hook uninstalled and says so, rather than hiding the wrong numbers.
        /// </para>
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void UnlockFillFn(IntPtr self, IntPtr before, IntPtr after, IntPtr methodInfo);

        private static NativeHook<UnlockFillFn> _unlockFill;
        private static UnlockFillFn _unlockFillTramp;

        /// <summary>Results-screen unlock fills seen, and how many of them were about a custom song.</summary>
        internal static long UnlockFills, UnlockHidden;

        /// <summary>
        /// The difficulty field in `_lH` (`_leb`), the enum member `_GB` hides on, and the record's
        /// size — all asked at installation and never written here.
        /// </summary>
        private static int _unlockDifficulty = -1;
        private static int _unlockNone = -1;
        private static int _unlockSize = -1;

        /// <summary>
        /// A bound on the snapshot this copies, chosen here rather than measured: the record is a
        /// handful of words, and anything larger is not the record this hook was written for.
        /// </summary>
        private const int UnlockSnapshotLimit = 64;

        private static bool InstallResultsUnlock()
        {
            IntPtr target = MethodResolver.ByName("DifficultyUnlockContainer", "_GB");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Error("the results screen's unlock progress will keep showing for custom songs");
                return false;
            }

            _unlockDifficulty = FieldResolver.Field("_lH", "_leb");
            _unlockNone = FieldResolver.EnumValue("_lH._MH", "_Ueb");
            _unlockSize = FieldResolver.ValueSize("_lH");

            if (_unlockDifficulty < 0 || _unlockDifficulty >= _unlockSize ||
                _unlockNone < 0 || _unlockNone > byte.MaxValue ||
                _unlockSize <= 0 || _unlockSize > UnlockSnapshotLimit)
            {
                Diagnostics.Error("the results screen's unlock progress will keep showing for custom songs");
                return false;
            }

            byte[] prologue = Prologue(target);
            _unlockFill = new NativeHook<UnlockFillFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)&UnlockFillDetour,
            };
            _unlockFill.Attach();
            _unlockFillTramp = _unlockFill.Trampoline;
            Diagnostics.Info("DifficultyUnlockContainer._GB hooked");
            return Landed(Hook.ResultsUnlock, target, prologue);
        }

        private static void DetachResultsUnlock()
        {
            _unlockFill?.Detach();
            _unlockFill = null;
            _unlockFillTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void UnlockFillDetour(IntPtr self, IntPtr before, IntPtr after, IntPtr methodInfo)
        {
            UnlockFills++;

            if (!Faulted)
            {
                try
                {
                    if (after != IntPtr.Zero &&
                        Selection.TryReadSong(out IntPtr playing) && CustomResults.IsOurs(playing))
                    {
                        UnlockHidden++;

                        // The game's own hide branch reads one byte of the new snapshot and, when it
                        // is the enum's "none", deactivates the container and returns. The copy keeps
                        // every other byte as the game wrote it.
                        byte* hidden = stackalloc byte[UnlockSnapshotLimit];
                        for (int i = 0; i < _unlockSize; i++) hidden[i] = ((byte*)after)[i];
                        hidden[_unlockDifficulty] = (byte)_unlockNone;

                        _unlockFillTramp(self, before, (IntPtr)hidden, methodInfo);
                        return;
                    }
                }
                catch (Exception e)
                {
                    Fault(Hook.ResultsUnlock, e);
                }
            }

            _unlockFillTramp(self, before, after, methodInfo);
        }
    }
}
