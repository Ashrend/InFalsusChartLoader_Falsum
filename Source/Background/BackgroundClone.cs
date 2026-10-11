using System;

namespace InFalsusChartLoader
{
    /// <summary>
    /// The material a background claim hands over: a clone of the game's own background material
    /// with this mod's picture on it.
    ///
    /// Why a clone and not a material built here — the game's background materials are authored:
    /// shader, render queue, z-test, blend, keywords, the `_Lowiro_AlphaMask*` slots and whatever
    /// else the build put on them, and a material built from a shader alone carries <b>none</b> of
    /// it. Measured on this build: the game's own background materials set `_ZTest = 4` and leave
    /// `_Lowiro_AlphaMaskTexture` authored-but-empty (the runtime fills that slot — the mask); a
    /// plain `Material` of the same shader defaults to z-test <i>disabled</i> and has no mask slot
    /// at all. Cloning the material the game itself just loaded for the reference is therefore the
    /// only way to be configured exactly as the game is, with nothing guessed.
    ///
    /// A video background is the same clone under a different owner — see `VideoBackground.Rebase`,
    /// which takes the clone over so the clip's own state machine drives it.
    /// </summary>
    internal static class BackgroundClone
    {
        /// <summary>
        /// Zero, with a reason, when the clone could not be made. A caller that gets zero leaves the
        /// game's own background in place, latches this mod's substitution off and reports the reason
        /// in one Release-visible Error — <b>never</b> the material built here: that material is the
        /// thing that hid this build's mask, and serving it on a failure would turn a loud failure
        /// back into the silent symptom this round exists to kill.
        /// </summary>
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
