using System;
using System.Collections.Generic;

namespace InFalsusChartLoader_falsum
{
    /// <summary>One custom pack and the songs that belong to it.</summary>
    internal sealed class ChartPack
    {
        /// <summary>The directory this pack came from.</summary>
        internal string Folder;

        /// <summary>The folder suffix after `[Pack]`, or `IFCL` for the legacy pack.</summary>
        internal string LogicalId;

        /// <summary>The five localized display names.</summary>
        internal string[] Names;

        /// <summary>Which pack-select column this pack belongs to.</summary>
        internal PackCategory Category = PackCategory.Story;

        /// <summary>Absolute path of `pack_cover.png`, or null when the pack has no cover.</summary>
        internal string CoverPath;

        /// <summary>Whether this is the legacy `IFCL` pack for root-level song folders.</summary>
        internal bool IsLegacy;

        /// <summary>The songs belonging to this pack.</summary>
        internal readonly List<ChartInfo> Charts = new List<ChartInfo>();

        /// <summary>The game pack id assigned during injection, or -1.</summary>
        internal int GamePackId = -1;

        /// <summary>The `packToAssets` row assigned during setup, or -1.</summary>
        internal int VisualRow = -1;

        /// <summary>Whether a custom cover was applied to this pack's visual row.</summary>
        internal bool HasCustomCover = false;

        /// <summary>The material built from `pack_cover.png`, or zero.</summary>
        internal IntPtr CoverMaterial = IntPtr.Zero;

        /// <summary>
        /// A copy of the packs, for a caller that is allowed to change them.
        /// </summary>
        /// <remarks>
        /// The commit path prunes the songs it was given and sorts the pack list in place, so a
        /// prepared snapshot — which is read again on the next visit — cannot be handed over
        /// directly: a song the game refused once would never come back until the folder changed.
        /// <para>
        /// The copy is therefore deep: a new list, a new pack per entry, and a new song list per
        /// pack. The songs themselves are shared, because nothing changes a song after it is
        /// imported and a chart's bytes are worth keeping in one place.
        /// </para>
        /// <para>
        /// The four fields the registration fills in — the game's ids and the cover material — are
        /// not carried over: they belong to one registration, and a copy has not been registered.
        /// </para>
        /// </remarks>
        internal static List<ChartPack> CopyForCommit(List<ChartPack> packs)
        {
            var copy = new List<ChartPack>(packs.Count);
            foreach (ChartPack pack in packs)
            {
                var one = new ChartPack
                {
                    Folder = pack.Folder,
                    LogicalId = pack.LogicalId,
                    Names = pack.Names,
                    Category = pack.Category,
                    CoverPath = pack.CoverPath,
                    IsLegacy = pack.IsLegacy,
                };

                one.Charts.AddRange(pack.Charts);
                copy.Add(one);
            }

            return copy;
        }
    }
}
