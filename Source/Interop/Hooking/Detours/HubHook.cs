using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`HubScene._WK` — the hub's song counts and its random jacket.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void HubRefreshFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<HubRefreshFn> _hubRefresh;
        private static HubRefreshFn _hubRefreshTramp;
        private static IntPtr _hubRefreshInfo;
        private static IntPtr _hub;

        private static bool InstallHub()
        {
            IntPtr info = MethodResolver.MethodInfoByRuntime("HubScene", "_WK", "Game.dll", "ifapp.Game.Scenes");
            if (info == IntPtr.Zero || *(IntPtr*)info == IntPtr.Zero)
            {
                Diagnostics.Warn("HubScene._WK could not be found; the hub's song counts will not follow " +
                                 "a reload");
                return false;
            }

            _hubRefreshInfo = info;
            IntPtr target = *(IntPtr*)info;
            byte[] prologue = Prologue(target);

            _hubRefresh = new NativeHook<HubRefreshFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&HubRefreshDetour,
            };
            _hubRefresh.Attach();
            _hubRefreshTramp = _hubRefresh.Trampoline;
            Diagnostics.Info("HubScene._WK hooked");
            return Landed(Hook.HubRefresh, target, prologue);
        }

        private static void DetachHub()
        {
            _hubRefresh?.Detach();
            _hubRefresh = null;
            _hubRefreshTramp = null;
            _hubRefreshInfo = IntPtr.Zero;
            _hub = IntPtr.Zero;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void HubRefreshDetour(IntPtr self, IntPtr methodInfo)
        {
            if (Memory.LooksLikeObject(self)) _hub = self;

            _hubRefreshTramp(self, methodInfo);
        }

        /// <summary>Recomputes the hub's counts when the tables changed while it is on screen.</summary>
        internal static void RefreshHub()
        {
            if (_hubRefreshInfo == IntPtr.Zero || !JacketFactory.IsAlive(_hub)) return;

            IntPtr raised = IntPtr.Zero;
            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_invoke(_hubRefreshInfo, _hub, null, ref raised);
            if (raised != IntPtr.Zero)
                Diagnostics.Error($"the hub's song counts could not be refreshed: {Raised.Text(raised)}");
            else
                Diagnostics.Info("probe: hub counts refreshed");
        }
    }
}
