using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>Watches <c>Charts/</c> and rebuilds the custom songs when the folder changed.</summary>
    /// <remarks>
    /// The reload is two phases. Nothing is written to the game's tables until every folder has been
    /// read and checked, so a bad zip leaves the previous songs alone. The commit then restores the
    /// shipped tables, drops every cache this mod keeps, and registers afresh.
    /// <para>
    /// It is entered from the <c>SongSelectScene.Awake</c> and <c>PackSelectScene.Awake</c> detours,
    /// through <see cref="Hooks"/>' shared <c>Apply</c>. Both run while a page is being built and before
    /// it reads the tables, which is the only safe moment: anything later — a timer, the frame loop, a
    /// scene switch — could land in the middle of a running song. Whichever wakes first does the work;
    /// the other is a fingerprint no-op.
    /// </para>
    /// </remarks>
    internal static class Reload
    {
        /// <summary>The folder being watched, or null before the first registration.</summary>
        private static string _root;

        /// <summary>The fingerprint of the folder as it was last applied.</summary>
        private static string _fingerprint;

        /// <summary>Set while a reload is running, so a second one cannot start inside it.</summary>
        private static bool _busy;

        /// <summary>Starts watching the folder the initial import used.</summary>
        internal static void Start(string root)
        {
            _root = root;
            ReloadPlan.TryFingerprint(root, out _fingerprint);
        }

        /// <summary>Rebuilds the songs when the folder changed since it was last applied.</summary>
        internal static void IfChanged()
        {
            if (string.IsNullOrEmpty(_root) || _busy || Hooks.Faulted) return;

            _busy = true;
            bool committed = false;
            try
            {
                // A folder that cannot be read is not an empty folder: keep everything as it is.
                if (!ReloadPlan.TryFingerprint(_root, out string now)) return;

#if DEBUG
                Diagnostics.Info($"song select awake: charts {(now == _fingerprint ? "unchanged" : "changed")}, " +
                                 $"payload {Selection.Describe()}");
                Diagnostics.Info($"text tables [{PackSetup.Counts()}]");
#endif

                if (now == _fingerprint) return;

                if (!SongCatalog.TryGetAssets(out IntPtr songData, out IntPtr packData)) return;

                var before = new List<string>(SongCatalog.ById.Count);
                foreach (KeyValuePair<ushort, ChartInfo> slot in SongCatalog.ById)
                    before.Add(slot.Value.BaseName);

                // Pack ids are positional and get recycled, so the name is what says whether the pack
                // the save selected still exists.
                var packNames = new Dictionary<int, string>();
                foreach (KeyValuePair<int, ChartPack> pack in SongCatalog.PacksById)
                    packNames[pack.Key] = pack.Value.LogicalId;

                // Prepare: read and check every folder. Nothing has been written yet, so a failure here
                // leaves the songs that are already registered exactly as they are.
                List<ChartPack> packs = ChartLibrary.Import(_root, ModCore.ValidateChartsOnly);
                if (packs == null) return;

                // Commit. The restore has to happen before the name admission runs: that check reads
                // the live song table, and only a restored table holds nothing but the game's own names.
#if DEBUG
                Probe.Tables(songData, packData, "on entry");
#endif
                if (SongCatalog.HasBaseline && !SongCatalog.RestoreBaseline(songData, packData))
                {
                    Diagnostics.Error("the game's tables could not be restored, so the charts were left " +
                                      "alone; the next visit to the song select tries again");
                    return;
                }

#if DEBUG
                Probe.Tables(songData, packData, "after restore");
#endif

                committed = true;

                SongCatalog.ResetCustom();
                ChartCatalog.Reset();
                AudioCatalog.Reset();
                JacketCatalog.Reset();
                BackgroundCatalog.Reset();
                VideoBackground.Reset();
                Selection.Reset();

                bool any = false;
                foreach (ChartPack pack in packs)
                    if (pack.Charts.Count > 0) any = true;

                bool applied = any ? ModCore.Register(songData, packData, packs) : true;

                // Either way this pass owns the tables now; the startup path must not register again.
                ModCore.MarkRegistered();

                if (!applied)
                {
                    // Put the shipped tables back so this visit is empty rather than half built, and
                    // leave the fingerprint alone so the next visit tries again.
                    if (SongCatalog.HasBaseline) SongCatalog.RestoreBaseline(songData, packData);
                    SongCatalog.ResetCustom();
                    SongCatalog.Refresh(songData);
                    PackSetup.RefreshLookup(packData);

                    Diagnostics.Error("the custom songs could not be registered; the charts folder will " +
                                      "be read again on the next visit to the song select");
                    return;
                }

                // Both indexes are derived from the tables, so they are rebuilt whether or not any
                // custom row went in.
                SongCatalog.Refresh(songData);
                PackSetup.RefreshLookup(packData);

#if DEBUG
                Probe.Tables(songData, packData, "after commit");
#endif

                foreach (KeyValuePair<int, string> pack in packNames)
                {
                    if (SongCatalog.PacksById.TryGetValue(pack.Key, out ChartPack current) &&
                        current.LogicalId == pack.Value) continue;

                    PackSetup.Forget(packData, pack.Key);
                    PackSetup.DeselectPackIfGone(pack.Key);
                }

                if (any)
                {
                    ResultKeys.Migrate(CustomResults.Table);
                    if (ResultKeys.Refiled > 0) CustomResults.Save();
                }

                var after = new List<string>();
                foreach (ChartPack pack in packs)
                    foreach (ChartInfo info in pack.Charts)
                        after.Add(info.BaseName);

                ReloadPlan.Compare(before, after, out int added, out int removed);
                int updated = after.Count - added;

                _fingerprint = now;
                Diagnostics.Load($"charts reloaded: +{added} ~{updated} -{removed}");
            }
            catch (Exception e)
            {
                if (committed && SongCatalog.TryGetAssets(out IntPtr data, out IntPtr packTable))
                {
                    // The tables were already replaced when this threw; put the shipped ones back so the
                    // page cannot be built from a table and a derived list that disagree.
                    if (SongCatalog.HasBaseline) SongCatalog.RestoreBaseline(data, packTable);
                    SongCatalog.ResetCustom();
                    SongCatalog.Refresh(data);
                    PackSetup.RefreshLookup(packTable);
                }

                Diagnostics.Error($"the charts could not be reloaded: {Diagnostics.Describe(e)}");
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
