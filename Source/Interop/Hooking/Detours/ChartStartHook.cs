using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`GameScene._rk` — the game hands play over to the running chart.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr RkFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<RkFn> _rk;
        private static RkFn _rkTramp;

        private static bool InstallChartStart()
        {
            IntPtr target = MethodResolver.ByName("GameScene", "_rk");
            if (target == IntPtr.Zero) return false;

            byte[] prologue = Prologue(target);
            _rk = new NativeHook<RkFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr>)&RkDetour,
            };
            _rk.Attach();
            _rkTramp = _rk.Trampoline;
            Diagnostics.Info("GameScene._rk hooked");
            return Landed(Hook.ChartStart, target, prologue);
        }

        private static void DetachChartStart()
        {
            _rk?.Detach();
            _rk = null;
            _rkTramp = null;
        }

        /// <summary>Starts this mod's background clip with the chart.</summary>
        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static IntPtr RkDetour(IntPtr self, IntPtr methodInfo)
        {
            ChartStarts++;

            if (!Faulted)
            {
                try
                {
                    VideoBackground.Restart();
                }
                catch (Exception e)
                {
                    Fault(Hook.ChartStart, e);
                }
            }

            return _rkTramp(self, methodInfo);
        }
    }
}
