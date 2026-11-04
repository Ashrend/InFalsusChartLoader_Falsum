using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`SongSelectScene.Awake` — the song select page is being built.</summary>
        /// <remarks>
        /// This is the one point where the tables can be swapped without the page having read them
        /// yet, which is why the reload runs here rather than after the page is up.
        /// </remarks>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void AwakeFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<AwakeFn> _awake;
        private static AwakeFn _awakeTramp;

        private static bool InstallSongSelectAwake()
        {
            IntPtr target = MethodResolver.ByRuntime("SongSelectScene", "Awake", "Game.dll",
                                                     "ifapp.Game.Scenes");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("SongSelectScene.Awake could not be resolved; a new chart needs a restart");
                return false;
            }

            byte[] prologue = Prologue(target);
            _awake = new NativeHook<AwakeFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&AwakeDetour,
            };
            _awake.Attach();
            _awakeTramp = _awake.Trampoline;
            Diagnostics.Info("SongSelectScene.Awake hooked");
            return Landed(Hook.SongSelectAwake, target, prologue);
        }

        private static void DetachSongSelectAwake()
        {
            _awake?.Detach();
            _awake = null;
            _awakeTramp = null;
        }

        /// <summary>Applies a pending chart change before the page reads the tables.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void AwakeDetour(IntPtr self, IntPtr methodInfo)
        {
            Apply(Hook.SongSelectAwake);
            _awakeTramp(self, methodInfo);
        }

        /// <summary>`PackSelectScene.Awake` — the pack column builds its cards here.</summary>
        /// <remarks>
        /// This page builds its pack list from the ids that exist when it wakes. If that happens
        /// before the song select page wakes, a pack added at runtime has no card at all, so the
        /// check has to run here too.
        /// </remarks>
        private static NativeHook<AwakeFn> _packAwake;
        private static AwakeFn _packAwakeTramp;

        private static bool InstallPackSelectAwake()
        {
            IntPtr target = MethodResolver.ByRuntime("PackSelectScene", "Awake", "Game.dll", "ifapp.Game");
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("PackSelectScene.Awake could not be resolved; a pack added at runtime " +
                                 "will have no card until the game is restarted");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packAwake = new NativeHook<AwakeFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&PackAwakeDetour,
            };
            _packAwake.Attach();
            _packAwakeTramp = _packAwake.Trampoline;
            Diagnostics.Info("PackSelectScene.Awake hooked");
            return Landed(Hook.PackSelectAwake, target, prologue);
        }

        private static void DetachPackSelectAwake()
        {
            _packAwake?.Detach();
            _packAwake = null;
            _packAwakeTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackAwakeDetour(IntPtr self, IntPtr methodInfo)
        {
            Apply(Hook.PackSelectAwake);
            _packAwakeTramp(self, methodInfo);
        }

        /// <summary>Runs a pending chart change, if there is one. A second call in the same visit is a no-op.</summary>
        private static void Apply(Hook hook)
        {
            if (Faulted) return;

            try
            {
                Reload.Commit();
            }
            catch (Exception e)
            {
                Fault(hook, e);
            }
        }
    }
}
