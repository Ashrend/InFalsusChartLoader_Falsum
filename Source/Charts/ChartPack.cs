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
        internal bool HasCustomCover;

        /// <summary>The material built from `pack_cover.png`, or zero.</summary>
        internal IntPtr CoverMaterial;
    }
}
