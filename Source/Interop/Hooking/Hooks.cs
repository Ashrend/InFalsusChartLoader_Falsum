using System;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The native hooks: which game functions are patched, and what happens when one faults.</summary>
    /// <remarks>Each detour lives in its own file under Detours/; every target is resolved by name.</remarks>
    internal static unsafe partial class Hooks
    {
        /// <summary>Set when a detour throws. Every detour stops calling into the mod after that.</summary>
        internal static bool Faulted { get; private set; }

        internal static int Faults { get; private set; }

        internal static long VaCalls, VaServed;
        internal static long DjaCalls, DjaHits;
        internal static long ZgaCalls, ZgaServed;
        internal static long ApACalls, ApAArmed;

        /// <summary>Jacket requests for a custom song that had no picture.</summary>
        internal static long ApaAsked;

        internal static long MiaCalls, MiaClaimed;

        internal static long ZoaCalls, ZoaArmed, LiaCalls, LiaClaimed;

        /// <summary>In-play background requests and how many were armed.</summary>
        internal static long UmaCalls, UmaArmed;

        /// <summary>Background requests for a custom song that could not be answered.</summary>
        internal static long UmaUnset;

        /// <summary>Chart starts.</summary>
        internal static long ChartStarts;

        /// <summary>Pauses and resumes of the scene's music.</summary>
        internal static long MusicPauses, MusicResumes;

#if DEBUG
        internal static long RowCalls, RowRedirects;
        internal static long TicksRow, TicksApa, TicksMia, TicksVc, TicksRead;

        internal static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

        /// <summary>A tick count converted to milliseconds.</summary>
        /// <remarks>
        /// The argument is a number of ticks that have passed, never a timestamp:
        /// <c>Now</c> is absolute, so a span has to be taken before it is converted.
        /// </remarks>
        internal static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

        /// <summary>How long each step of one native attach took.</summary>
        /// <remarks>
        /// <see cref="NativeHook{T}.Attach"/> fills this in. The MelonLoader branch has no separate
        /// Dobby call and no separate delegate construction, so only <see cref="TotalMs"/> is set and
        /// the two sub-parts stay zero there.
        /// </remarks>
        internal struct AttachTiming
        {
            /// <summary>The native inline patch (<c>Dobby.Hook</c>).</summary>
            internal double DobbyMs;

            /// <summary>Building the callable delegate over the trampoline.</summary>
            internal double TrampolineMs;

            /// <summary>The whole attach; this is the only measured number on MelonLoader.</summary>
            internal double TotalMs;
        }
#endif

        /// <summary>The attach timings appended to a hook line in Debug, or nothing in Release.</summary>
        internal static string Timing<T>(NativeHook<T> hook, long prologueTicks) where T : Delegate
        {
#if DEBUG
            AttachTiming timing = hook.Timing;
            return $"  prologue={Ms(prologueTicks):F1} ms dobby={timing.DobbyMs:F1} ms " +
                   $"trampoline={timing.TrampolineMs:F1} ms attach={timing.TotalMs:F1} ms";
#else
            return "";
#endif
        }

        /// <summary>The combined attach timings for the one line that covers two hooks.</summary>
        /// <remarks>The line follows both attaches, so the numbers are their sum; neither has a prologue read.</remarks>
        internal static string Timing<T, U>(NativeHook<T> first, NativeHook<U> second)
            where T : Delegate
            where U : Delegate
        {
#if DEBUG
            AttachTiming a = first.Timing;
            AttachTiming b = second.Timing;
            return $"  prologue=0.0 ms dobby={(a.DobbyMs + b.DobbyMs):F1} ms " +
                   $"trampoline={(a.TrampolineMs + b.TrampolineMs):F1} ms " +
                   $"attach={(a.TotalMs + b.TotalMs):F1} ms";
#else
            return "";
#endif
        }

        /// <summary>How many hooks went in, and how many were tried.</summary>
        /// <remarks>These are the first tier's numbers, and the startup line uses them.</remarks>
        internal static int Installed { get; private set; }
        internal static int Attempted { get; private set; }

        /// <summary>Serialises the deferred install, which is entered from a game callback.</summary>
        private static readonly object Gate = new object();

        /// <summary>Whether the deferred tier has been installed; a second call must attach nothing.</summary>
        private static bool _deferredInstalled;

        internal static void Install()
        {
#if DEBUG
            long started = Now;
#endif

            int installed = 0;
            int attempted = 0;

            void Count(bool ok) { attempted++; if (ok) installed++; }

            // The first tier is what the song select itself uses: the two Awake detours that make
            // the commit point, the selection handler, the pack card layout, the audio reader and the
            // jacket handles, and the scene-switch probe. Measured on a session that opened the song
            // select without playing anything: every one of these fired, and every hook left out below
            // stayed at zero. Each attach costs about 400 ms, so the split is what keeps the game's
            // startup from paying for hooks that cannot be reached before a song is picked.
            Count(InstallAudioLoad());        // zga=1191, dja=13     -> fired
            Count(InstallJacket());           // _apA=1, _MIA=1       -> fired; the other three jacket
                                              //                        hooks are deferred (zoa/lia/uma=0)
            Count(InstallSongSelect());       // apply=0, but this is the entry point for picking a
                                              // song: the measured session never picked one
            Count(InstallSongSelectAwake());  // the commit point
            Count(InstallPackSelectAwake());  // the commit point
            Count(InstallPackVisual());       // rowN=1               -> fired
            Count(InstallSceneSwitch());      // switch=12949         -> fired

            Installed = installed;
            Attempted = attempted;

            // The addresses resolved above are what the next start skips, so they are written
            // down once all of them are in. Nothing is written when no target was resolved here,
            // and nothing at all when the game module could not be keyed on.
            AddressCache.Flush();

#if DEBUG
            // Every address this install needed has been asked for by now, so the cache can
            // say what it did: restored, resolved, or refused, and whether it loaded at all.
            AddressCache.Report();
            Diagnostics.Info($"hooks installed in {Ms(Now - started):F0} ms");
#endif

            Diagnostics.Load($"{installed}/{attempted} hooks installed");
            if (installed < attempted)
                Diagnostics.Warn("a hook is missing; whatever it drives will silently do nothing");
        }

        /// <summary>Installs the hooks no song select screen can reach before a song is picked.</summary>
        /// <remarks>
        /// Every hook here stayed at zero for the measured session that opened the song select and
        /// never played a song, so none of them can be needed before then. The caller may re-enter
        /// (the song select's <c>Awake</c> runs this), and a second attach would build a second
        /// <see cref="NativeHook{T}"/> over the same code: the first would leak and the code could be
        /// patched twice. The flag and the lock make a second call attach nothing.
        /// <see cref="Installed"/> and <see cref="Attempted"/> are deliberately not touched: they
        /// describe the first tier, which is what the startup line has to keep showing.
        /// </remarks>
        internal static void InstallDeferred()
        {
            lock (Gate)
            {
                // Set before the attaches, not after: a re-entrant call on this same thread has to
                // find the flag already up, or it would attach the whole tier again. The lock is
                // what keeps a call from another thread out.
                if (_deferredInstalled) return;
                _deferredInstalled = true;

#if DEBUG
                long started = Now;
#endif

                InstallChartLoad();        // va=0
                InstallChartStart();       // cs=0
                InstallPause();            // hold=0/0
                InstallPlayGate();         // gate=0/0
                InstallDifficultyState();  // state=0/0
                InstallCustomResults();    // upd/enc/saved=0
                InstallResultsUnlock();    // unlock=0/0

                // These three used to be chained off the end of InstallJacket(); all three were at
                // zero in the measured session, so they belong after the commit with the rest.
                InstallChartReference();   // zoa=0
                InstallChartLoader();      // lia=0
                InstallBackground();       // uma=0

                // The addresses resolved above are what the next visit to the song select skips,
                // so they are written down now that all of them are in. A cache that took them all
                // from the file has nothing to write, and Flush is a no-op then.
                AddressCache.Flush();

#if DEBUG
                Diagnostics.Info($"deferred hooks installed in {Ms(Now - started):F0} ms");
#endif
            }
        }

        internal static void Uninstall()
        {
            DetachSceneSwitch();
            DetachPlayGate();
            DetachResultsUnlock();
            DetachCustomResults();
            DetachPackVisual();
            DetachDifficultyState();
            DetachSongSelectAwake();
            DetachPackSelectAwake();
            DetachSongSelect();
            DetachJacket();
            DetachAudioLoad();
            DetachPause();
            DetachChartStart();
            DetachChartLoad();
        }

        /// <summary>The first bytes at a hook target.</summary>
        internal static unsafe byte[] Prologue(IntPtr target, int count = 8)
        {
            if (target == IntPtr.Zero) return null;

            var bytes = new byte[count];
            for (int i = 0; i < count; i++) bytes[i] = *(byte*)(target + i);
            return bytes;
        }

        /// <summary>The first bytes at a hook target, and how many ticks the read took.</summary>
        /// <remarks>
        /// The ticks are a span, so <c>Ms(ticks)</c> is right. They stay zero in Release, where this
        /// overload exists only to keep the installers' timing call sites compiling.
        /// </remarks>
        internal static unsafe byte[] Prologue(IntPtr target, out long ticks, int count = 8)
        {
            ticks = 0;
#if DEBUG
            long started = Now;
#endif
            byte[] bytes = Prologue(target, count);
#if DEBUG
            ticks = Now - started;
#endif
            return bytes;
        }

        /// <summary>Whether an attach changed the target.</summary>
        private static unsafe bool Patched(IntPtr target, byte[] prologue)
        {
            if (target == IntPtr.Zero || prologue == null) return false;

            for (int i = 0; i < prologue.Length; i++)
                if (*(byte*)(target + i) != prologue[i]) return true;
            return false;
        }

        /// <summary>The hooks, named for a detour that did not land.</summary>
        internal enum Hook
        {
            ChartLoader,
            AudioRead,
            AudioName,
            JacketReference,
            JacketMaterial,
            ChartReference,
            ChartMaterial,
            Background,
            ChartStart,
            MusicPause,
            MusicResume,
            SongSelectApply,
            SongSelectAwake,
            PackSelectAwake,
            DifficultyState,
            PackVisual,
            PackRow,
            ResultRead,
            ResultReadFull,
            ResultUpdate,
            EncounterResult,
            ResultsUnlock,
            PlayGate,
            SceneSwitch,
        }

        /// <summary>Whether a detour landed.</summary>
        private static bool Landed(Hook hook, IntPtr target, byte[] prologue)
        {
            if (Patched(target, prologue)) return true;

            NotLanded(hook);
            return false;
        }

        /// <summary>Says which hook did not take.</summary>
        [System.Diagnostics.Conditional("DEBUG")]
        private static void NotLanded(Hook hook)
        {
#if DEBUG
            Diagnostics.Warn($"{Name(hook)} was attached but its first bytes are unchanged; the detour " +
                             "is not in place, and whatever it answers for will silently do nothing");
#endif
        }

#if DEBUG
        /// <summary>The hooks by name, for the log. Debug only —see <see cref="Hook"/>.</summary>
        private static string Name(Hook hook) => hook switch
        {
            Hook.ChartLoader => "_s._VA",
            Hook.AudioRead => "_J._hF._ZgA",
            Hook.AudioName => "_BG._DJA",
            Hook.JacketReference => "_apA",
            Hook.JacketMaterial => "_MIA",
            Hook.ChartReference => "SongData._ZOA",
            Hook.ChartMaterial => "AddressableHandleAutoReleaser._LIA",
            Hook.Background => "GameplayBackgrounds._UmA",
            Hook.ChartStart => "GameScene._rk",
            Hook.MusicPause => "_Dg._hiA",
            Hook.MusicResume => "_Dg._KiA",
            Hook.SongSelectApply => "SongSelectScene._MN",
            Hook.SongSelectAwake => "SongSelectScene.Awake",
            Hook.PackSelectAwake => "PackSelectScene.Awake",
            Hook.DifficultyState => "_NH._UoA",
            Hook.PackVisual => "PackVisualMemberLarge._vc",
            Hook.PackRow => "the pack row accessor (SongSelectPackAssets[])",
            Hook.ResultRead => "GameResultsV4.TryGetResult(song)",
            Hook.ResultReadFull => "GameResultsV4.TryGetResult(song, full)",
            Hook.ResultUpdate => "GameResultsV4.TryUpdate",
            Hook.EncounterResult => "EncounterResults.UpdateEncounterResult",
            Hook.ResultsUnlock => "DifficultyUnlockContainer._GB",
            Hook.PlayGate => "SongSelectScene._sN",
            Hook.SceneSwitch => "CoreScene._CB.MoveNext",
            _ => "(unknown hook)",
        };
#endif

        /// <summary>Reports a detour fault once, then stops calling into the mod.</summary>
        private static void Fault(Hook hook, Exception e)
        {
            Faulted = true;
            Faults++;

            if (Faults > 1) return;
            Diagnostics.Error($"{Which(hook)}detour faulted, custom charts disabled: {Diagnostics.Describe(e)}");
        }

        /// <summary>The hook's name in Debug, or an article in Release.</summary>
        private static string Which(Hook hook)
        {
#if DEBUG
            return Name(hook) + " ";
#else
            return "a ";
#endif
        }
    }
}
