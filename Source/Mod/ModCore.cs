using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The mod: installs hooks and registers the songs when the tables are written.</summary>
    internal static class ModCore
    {
        /// <summary>The mod's version, shared by both loader entries.</summary>
        internal const string Version = "0.5.0";

        /// <summary>The packs being registered; each commit replaces them.</summary>
        private static List<ChartPack> _imported = new List<ChartPack>();

        /// <summary>Whether the custom results' save container is ready.</summary>
        private static bool _resultsReady;

        /// <summary>Whether the deferred hook tier has been asked for.</summary>
        private static bool _deferredAsked;

#if DEBUG
        /// <summary>Frames seen, for the probe's timer.</summary>
        private static int _frames;
#endif

        /// <summary>Reports that the mod loaded.</summary>
        internal static void Loaded()
        {
            Diagnostics.Load("InFalsusChartLoader_falsum loading...");
        }

        /// <summary>Installs the hooks, and starts the background read of the folders.</summary>
        internal static void Initialize()
        {
            try
            {
                bool codecReady = ChartCodec.Resolve();
                if (!codecReady)
                {
                    Diagnostics.Error("the game's chart decoder could not be reached; no charts will load");
                    return;
                }

                bool assetsReady = StreamingAssets.Resolve();

                string root = ChartLibrary.Locate();
                Diagnostics.Info($"charts folder: {root}");

                // The tables are written from the song select, which is the only moment it is safe to
                // touch them. Until then the mod has nothing but its hooks in place - and the
                // folders, read on a worker thread.
                Reload.Start(root);

                // The folders are read on a worker thread while the game is still starting, so the
                // songs are already there when the player reaches the song select. The worker only
                // reads files: the check that needs the game stays on the main thread.
                if (!string.IsNullOrEmpty(root))
                    Prepare.Start(root, r => ChartLibrary.Import(r, Prepare.NeverFails), ChartCodec.Decoder);

                if (!string.IsNullOrEmpty(root)) Hooks.Install();
                else Diagnostics.Warn("there is no charts folder to watch, so no hooks are being installed");

#if DEBUG
                Probe.Startup(root, codecReady, assetsReady, Hooks.Installed, Hooks.Attempted);
#endif
            }
            catch (Exception e)
            {
                Diagnostics.Error($"chart loading failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Prepares the custom results once the game's tables exist.</summary>
        internal static void Update()
        {
            VideoBackground.Follow();

            // The hooks the song select cannot reach are installed here, on the first frame after
            // startup, and not at the first commit: the commit runs inside the song select's Awake,
            // and an attach there measured six times what it costs anywhere else (1,900 ms against
            // 320 ms for the very same call). The song select's Awake is the busiest moment in the
            // game - it is loading the screen - so the tier is installed before the player gets
            // anywhere near it.
            if (!_deferredAsked)
            {
                _deferredAsked = true;
                Diagnostics.Info("deferred hooks: installing on the first frame");
                Hooks.InstallDeferred();
            }

            // The background import is advanced before anything else can return: this is what takes a
            // finished snapshot, flushes the log lines the worker held back, and checks a few more songs
            // within this frame's budget. Skipping it on any early return would stall the import and
            // lose the worker's log.
            Prepare.Collect();
            Prepare.ValidateSlice(Prepare.DefaultBudgetMs);

#if DEBUG
            if (++_frames % 600 == 0) Diagnostics.Info("probe: counters       " + Probe.Status());
#endif

            // The tables are written by Reload, from the song select. The one thing that has to
            // happen early is the custom results' save container: the game asks for scores long
            // before anything else, and the container is cheap to make.
            if (_resultsReady) return;

            try
            {
                if (!SongCatalog.TryGetAssets(out _, out _))
                {
                    // The game will never have these tables: stop asking every frame.
                    if (SongCatalog.Unusable)
                    {
                        _resultsReady = true;
                        Diagnostics.Error("the game's song and pack tables could not be found; the custom " +
                                          "songs will not be registered");
                    }
                    return;
                }

                CustomResults.Prepare();
                _resultsReady = true;
            }
            catch (Exception e)
            {
                _resultsReady = true;      // do not try again every frame
                Diagnostics.Error($"the custom results could not be prepared: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>
        /// Puts <paramref name="packs"/> into the game's tables and returns whether any song made it.
        /// </summary>
        /// <remarks>
        /// The reload calls this with freshly imported packs. The shipped tables are copied before the
        /// first injection; a later reload restores them first.
        /// </remarks>
        internal static bool Register(IntPtr songData, IntPtr packData, List<ChartPack> packs)
        {
            _imported = packs ?? new List<ChartPack>();
            if (_imported.Count == 0) return false;

#if DEBUG
            PackSetup.TryGetVisuals(out IntPtr packAssetsForProbe);
            Probe.Before(songData, packData, packAssetsForProbe);
#endif

            Diagnostics.Load(FieldResolver.Stats());

            List<ChartInfo> named = SongCatalog.SettleNames(Flatten(_imported), songData);
            if (named.Count == 0)
            {
                Diagnostics.Error("no custom song could be registered");
                return false;
            }

            KeepOnly(named);
            if (_imported.Count == 0)
            {
                Diagnostics.Error("no custom song could be registered");
                return false;
            }

            List<ChartInfo> ready = RegisterAudio(Flatten(_imported));
            if (ready.Count == 0)
            {
                Diagnostics.Error("no custom song could have its audio registered");
                return false;
            }

            KeepOnly(ready);
            if (_imported.Count == 0)
            {
                Diagnostics.Error("no custom song could have its audio registered");
                return false;
            }

            // Pictures are built here, not before name admission: two folders with the same id would
            // otherwise let the refused one leave the admitted song wearing its artwork.
            foreach (ChartInfo info in ready)
            {
                JacketCatalog.Add(info, out _);
                BackgroundCatalog.Add(info, out _);
            }

#if DEBUG
            foreach (ChartInfo info in ready)
                Probe.Imported(info, AudioCatalog.IndexOf(info.BaseName), AudioCatalog.BytesOf(info.BaseName),
                               JacketCatalog.Count);
#endif

            if (!SongCatalog.CaptureBaseline(songData, packData))
            {
                Diagnostics.Error("the game's tables could not be copied, so nothing was added: a reload " +
                                  "would have no clean state to go back to");
                return false;
            }

            InjectionResult injected = SongCatalog.Inject(songData, packData, _imported);

            if (injected.SongsAdded == 0)
            {
                Diagnostics.Error("the custom songs could not be added to the game's song list");
                return false;
            }

            if (injected.PacksAdded == 0)
            {
                Diagnostics.Error("the custom pack could not be added; the custom songs are in the " +
                                  "song list but have no pack of their own");
            }
            else
            {
                if (PackSetup.TryGetVisuals(out IntPtr packAssets))
                {
                    foreach (ChartPack pack in _imported)
                    {
                        if (!PackSetup.ExtendVisuals(packAssets, pack))
                            Diagnostics.Warn($"pack '{pack.LogicalId}' visual row could not be prepared");
                    }
                }
                else
                {
                    Diagnostics.Warn("the pack visual table could not be reached");
                }

                PackSetup.RefreshLookup(packData);
                foreach (ChartPack pack in _imported)
                    PackSetup.SetPackName(pack.GamePackId, pack.Names);
            }

#if DEBUG
            PackSetup.TryGetVisuals(out IntPtr packAssetsAfter);
            Probe.After(songData, packData, packAssetsAfter, ready);
#endif

            Diagnostics.Load($"packs: {injected.PacksAdded} registered " +
                             $"({injected.CustomPacksAdded} custom, {injected.LegacyPacksAdded} IFCL), " +
                             $"songs: {injected.SongsAdded} registered");

            return true;
        }

        /// <summary>All songs in pack order.</summary>
        private static List<ChartInfo> Flatten(List<ChartPack> packs)
        {
            var songs = new List<ChartInfo>();
            foreach (ChartPack pack in packs)
                songs.AddRange(pack.Charts);
            return songs;
        }

        /// <summary>Drops songs the game or the audio registry refused.</summary>
        private static void KeepOnly(List<ChartInfo> keep)
        {
            var allowed = new HashSet<ChartInfo>(keep);
            foreach (ChartPack pack in _imported)
                pack.Charts.RemoveAll(chart => !allowed.Contains(chart));
            _imported.RemoveAll(pack => pack.Charts.Count == 0);
        }

        /// <summary>Stops every detour. The loader calls this once, on the way out.</summary>
        internal static void Shutdown()
        {
            Hooks.Uninstall();
        }

        /// <summary>Registers audio for the songs and returns the ones that got it.</summary>
        private static List<ChartInfo> RegisterAudio(List<ChartInfo> charts)
        {
            if (!StreamingAssets.Resolve())
            {
                Diagnostics.Error("the game's asset manager could not be reached; no custom audio can " +
                                  "be registered, so no custom song can be played");
                return new List<ChartInfo>();
            }

            var ready = new List<ChartInfo>(charts.Count);
            foreach (ChartInfo info in charts)
            {
                if (AudioCatalog.Add(info.BaseName, info.SongPath, out string reason)) ready.Add(info);
                else Diagnostics.Error($"'{info.Name}' was left out: {reason}");
            }

            Diagnostics.Info($"audio registered for {ready.Count} of {charts.Count} songs");
            return ready;
        }

        /// <summary>Checks the charts a folder imports, without touching any picture cache.</summary>
        /// <remarks>
        /// The reload calls this while it is still deciding: a folder that fails here never reaches the
        /// commit, so nothing half-read is registered. Pictures are built later, by <see cref="Register"/>.
        /// <para>
        /// The check itself is <see cref="ChartCheck"/>, the same code the background preparation uses,
        /// so a chart that one admits is admitted here too.
        /// </para>
        /// </remarks>
        internal static string ValidateChartsOnly(ChartInfo info)
        {
            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (!ChartCheck.Try(info, d, ChartCodec.Decoder, out string reason)) return reason;
            }

            return null;
        }
    }
}
