using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The mod: imports chart folders, installs hooks, and registers the songs.</summary>
    internal static class ModCore
    {
        /// <summary>The mod's version, shared by both loader entries.</summary>
        internal const string Version = "0.3.0";

        /// <summary>The packs and songs waiting for the game's data to exist.</summary>
        private static List<ChartPack> _imported = new List<ChartPack>();

        private static bool _registered;

#if DEBUG
        /// <summary>Frames seen, for the probe's timer.</summary>
        private static int _frames;
#endif

        /// <summary>Reports that the mod loaded.</summary>
        internal static void Loaded()
        {
            Diagnostics.Load("InFalsusChartLoader_falsum loading...");
        }

        /// <summary>Imports the chart folders and installs the hooks.</summary>
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

                _imported = ChartLibrary.Import(root, Validate);
                int importedSongs = CountSongs(_imported);
                Diagnostics.Load($"packs: {_imported.Count} imported, songs: {importedSongs} imported");

                bool worthHooking = importedSongs > 0;
                if (worthHooking) Hooks.Install();
                else Diagnostics.Info("no charts were imported, so no hooks are being installed");

#if DEBUG
                Probe.Startup(root, codecReady, assetsReady, Hooks.Installed, Hooks.Attempted);
#endif
            }
            catch (Exception e)
            {
                Diagnostics.Error($"chart loading failed: {Diagnostics.Describe(e)}");
            }
        }

        /// <summary>Registers the songs once the game's tables exist.</summary>
        internal static void Update()
        {
            VideoBackground.Follow();

#if DEBUG
            if (++_frames % 600 == 0) Diagnostics.Info("probe: counters       " + Probe.Status());
#endif

            if (_registered || _imported.Count == 0) return;

            try
            {
                if (!SongCatalog.TryGetAssets(out IntPtr songData, out IntPtr packData))
                {
                    if (SongCatalog.Unusable) _registered = true;
                    return;
                }

#if DEBUG
                PackSetup.TryGetVisuals(out IntPtr packAssetsForProbe);
                Probe.Before(songData, packData, packAssetsForProbe);
#endif

                Diagnostics.Load(FieldResolver.Stats());

                List<ChartInfo> named = SongCatalog.SettleNames(Flatten(_imported), songData);
                if (named.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could be registered");
                    return;
                }

                KeepOnly(named);
                if (_imported.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could be registered");
                    return;
                }

                List<ChartInfo> ready = RegisterAudio(Flatten(_imported));
                if (ready.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could have its audio registered");
                    return;
                }

                KeepOnly(ready);
                if (_imported.Count == 0)
                {
                    _registered = true;
                    Diagnostics.Error("no custom song could have its audio registered");
                    return;
                }

                InjectionResult injected = SongCatalog.Inject(songData, packData, _imported);
                _registered = true;

                if (injected.SongsAdded == 0)
                {
                    Diagnostics.Error("the custom songs could not be added to the game's song list");
                    return;
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

                CustomResults.Prepare();

#if DEBUG
                PackSetup.TryGetVisuals(out IntPtr packAssetsAfter);
                Probe.After(songData, packData, packAssetsAfter, ready);
#endif

                Diagnostics.Load($"packs: {injected.PacksAdded} registered " +
                                 $"({injected.CustomPacksAdded} custom, {injected.LegacyPacksAdded} IFCL), " +
                                 $"songs: {injected.SongsAdded} registered");
            }
            catch (Exception e)
            {
                _registered = true;
                Diagnostics.Error($"registering the custom songs failed: {Diagnostics.Describe(e)}");
            }
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

        /// <summary>The number of songs across all imported packs.</summary>
        private static int CountSongs(List<ChartPack> packs)
        {
            int count = 0;
            foreach (ChartPack pack in packs) count += pack.Charts.Count;
            return count;
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

        /// <summary>Checks the charts a folder imports.</summary>
        private static string Validate(ChartInfo info)
        {
            byte[] scratch = new byte[32];
            for (int d = 0; d < ChartInfo.Difficulties; d++)
            {
                if (info.NoChart[d]) continue;

                string path = info.ChartPaths[d];

                string seed = info.ChartNames[d];

                byte[] bytes = info.ChartBytes != null ? info.ChartBytes[d] : null;
                if (bytes == null)
                {
                    try
                    {
                        bytes = System.IO.File.ReadAllBytes(path);
                    }
                    catch (Exception e)
                    {
                        return $"difficulty {d} could not be read: {Diagnostics.Describe(e)}";
                    }
                }

                unsafe
                {
                    fixed (byte* tuple = scratch)
                    {
                        if (!ChartCodec.TryDecode(bytes, seed, (IntPtr)tuple, out string reason))
                            return $"difficulty {d}: {reason}";
                    }
                }
            }

            if (!JacketCatalog.Add(info, out string jacketReason))
                Diagnostics.Warn($"'{info.Name}' has no jacket of its own: {jacketReason}");

            if (!BackgroundCatalog.Add(info, out string backgroundReason))
                Diagnostics.Warn($"'{info.Name}' has no background of its own: {backgroundReason}");

#if DEBUG
            Probe.Imported(info, AudioCatalog.IndexOf(info.BaseName), AudioCatalog.BytesOf(info.BaseName),
                           JacketCatalog.Count);
#endif

            return null;
        }
    }
}
