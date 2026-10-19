using System;

namespace InFalsusChartLoader
{
    /// <summary>Clones the game's background material and puts this mod's picture on it.</summary>
    internal static class BackgroundClone
    {
        /// <summary>Builds the material, or zero with a reason when it cannot.</summary>
        internal static IntPtr Build(IntPtr gameMaterial, IntPtr ours, out string reason)
        {
            reason = null;

            if (VideoBackground.Owns(ours)) return VideoBackground.Rebase(gameMaterial, out reason);

            IntPtr texture = JacketFactory.MainTexture(ours, out reason);
            if (texture == IntPtr.Zero) return IntPtr.Zero;

            IntPtr clone = JacketFactory.CloneFrom(gameMaterial, out reason);
            if (clone == IntPtr.Zero) return IntPtr.Zero;

            if (!JacketFactory.SetMainTexture(clone, texture, out reason)) return IntPtr.Zero;

            return clone;
        }
    }
}
