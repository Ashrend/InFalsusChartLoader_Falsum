using System;

namespace InFalsusChartLoader_falsum
{
    /// <summary>A native hook: it detours a game function and offers the function it replaced.</summary>
    internal sealed unsafe class NativeHook<T> where T : Delegate
    {
#if MELONLOADER
        private readonly MelonLoader.NativeUtils.NativeHook<T> _hook = new();

        public IntPtr Target { set => _hook.Target = value; }

        public IntPtr Detour { set => _hook.Detour = value; }

        public T Trampoline => _hook.Trampoline;

#if DEBUG
        /// <summary>How long the whole attach took. MelonLoader has no split to report.</summary>
        internal Hooks.AttachTiming Timing;
#endif

        public void Attach()
        {
#if DEBUG
            Timing = default;
            long started = Hooks.Now;
#endif
            _hook.Attach();
#if DEBUG
            Timing.DobbyMs = 0.0;
            Timing.TrampolineMs = 0.0;
            Timing.TotalMs = Hooks.Ms(Hooks.Now - started);
#endif
        }

        public void Detach() => _hook.Detach();
#elif BEPINEX
        private static class Dobby
        {
            private static readonly IntPtr Library = Load();

            internal static readonly delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int> Hook =
                (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr*, int>)
                    System.Runtime.InteropServices.NativeLibrary.GetExport(Library, "DobbyHook");

            internal static readonly delegate* unmanaged[Cdecl]<IntPtr, int> Destroy =
                (delegate* unmanaged[Cdecl]<IntPtr, int>)
                    System.Runtime.InteropServices.NativeLibrary.GetExport(Library, "DobbyDestroy");

            private static IntPtr Load()
            {
                if (System.Runtime.InteropServices.NativeLibrary.TryLoad("dobby.dll", out IntPtr loaded))
                    return loaded;

                return System.Runtime.InteropServices.NativeLibrary.Load(
                    System.IO.Path.Combine(BepInEx.Paths.BepInExAssemblyDirectory, "dobby.dll"));
            }
        }

        private IntPtr _target;
        private T _trampoline;

        public IntPtr Target { set => _target = value; }

        public IntPtr Detour { private get; set; }

        public T Trampoline => _trampoline;

#if DEBUG
        /// <summary>What the last <see cref="Attach"/> spent its time on.</summary>
        internal Hooks.AttachTiming Timing;
#endif

        public void Attach()
        {
#if DEBUG
            Timing = default;
            long started = Hooks.Now;
#endif
            if (_target == IntPtr.Zero || Detour == IntPtr.Zero) return;

            IntPtr origin;
            int patched = Dobby.Hook(_target, Detour, &origin);
#if DEBUG
            long patchedAt = Hooks.Now;
            Timing.DobbyMs = Hooks.Ms(patchedAt - started);
            Timing.TotalMs = Timing.DobbyMs;
#endif
            if (patched != 0) return;

            _trampoline = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(origin);
#if DEBUG
            Timing.TrampolineMs = Hooks.Ms(Hooks.Now - patchedAt);
            Timing.TotalMs = Hooks.Ms(Hooks.Now - started);
#endif
        }

        public void Detach()
        {
            if (_target == IntPtr.Zero) return;

            Dobby.Destroy(_target);
            _target = IntPtr.Zero;
            _trampoline = default;
        }
#else
#error Define MELONLOADER or BEPINEX.
#endif
    }
}
