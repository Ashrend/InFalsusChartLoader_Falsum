using System;

namespace InFalsusChartLoader
{
    /// <summary>
    /// The one place a detour is attached, so the twenty-two detours do not name a loader.
    ///
    /// <para>
    /// Every member this class exposes is exactly what the detour files already used from
    /// `MelonLoader.NativeUtils.NativeHook&lt;T&gt;`: an initializer that sets <see cref="Target"/>
    /// and <see cref="Detour"/>, <see cref="Attach"/>, <see cref="Trampoline"/>, and
    /// <see cref="Detach"/>.
    /// </para>
    /// <para>
    /// Both backends use Dobby. The MelonLoader one forwards to `MelonLoader.NativeUtils.NativeHook`,
    /// which is Dobby underneath. The BepInEx one calls the `dobby.dll` BepInEx ships in its own
    /// core folder. That is not a preference: the first BepInEx build used
    /// `MonoMod.RuntimeDetour.NativeDetour`, and the game died with
    /// "attempted to call a UnmanagedCallersOnly method from managed code" the first time a hooked
    /// method was reached (the minidump's crash thread has the IL2CPP runtime as its return
    /// address). The detours and their bodies are the same ones the MelonLoader build has run for
    /// months, so the difference was the engine, and Dobby is the engine known to work here.
    /// </para>
    /// </summary>
    internal sealed unsafe class NativeHook<T> where T : Delegate
    {
#if MELONLOADER
        private readonly MelonLoader.NativeUtils.NativeHook<T> _hook = new();

        public IntPtr Target { set => _hook.Target = value; }

        public IntPtr Detour { set => _hook.Detour = value; }

        public T Trampoline => _hook.Trampoline;

        public void Attach() => _hook.Attach();

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
                // BepInEx loads its own copy; by name returns that one. The path is the fallback for
                // a host that has not loaded it yet.
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

        public void Attach()
        {
            if (_target == IntPtr.Zero || Detour == IntPtr.Zero) return;

            IntPtr origin;
            if (Dobby.Hook(_target, Detour, &origin) != 0) return;   // `Landed` reports it

            _trampoline = System.Runtime.InteropServices.Marshal.GetDelegateForFunctionPointer<T>(origin);
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
