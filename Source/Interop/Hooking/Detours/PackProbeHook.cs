using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    internal static unsafe partial class Hooks
    {
        private const string PackProbeImage = "Game.dll";
        private const string PackProbeNamespace = "ifapp.Game";

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackCardFn(IntPtr self, IntPtr dataAccess, IntPtr viewModel, int index,
                                        IntPtr methodInfo);

        private static NativeHook<PackCardFn> _packCard;
        private static PackCardFn _packCardTramp;

        private static readonly HashSet<int> _packCardsSeen = new HashSet<int>();

        private static bool InstallPackProbe()
        {
            IntPtr target = MethodResolver.ByRuntime("PackVisualMemberSmall", "_bC", PackProbeImage, PackProbeNamespace);
            if (target == IntPtr.Zero) return false;

            byte[] prologue = Prologue(target);
            _packCard = new NativeHook<PackCardFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, IntPtr, void>)
                    &PackCardDetour,
            };
            _packCard.Attach();
            _packCardTramp = _packCard.Trampoline;
            Diagnostics.Info("PackVisualMemberSmall._bC hooked (debug pack state)");
            return Landed(Hook.PackVisual, target, prologue);
        }

        private static void DetachPackProbe()
        {
            _packCard?.Detach();
            _packCard = null;
            _packCardTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackCardDetour(IntPtr self, IntPtr dataAccess, IntPtr viewModel, int index,
                                           IntPtr methodInfo)
        {
            _packCardTramp(self, dataAccess, viewModel, index, methodInfo);

            try
            {
                if (viewModel == IntPtr.Zero) return;
                int id = Memory.U16(viewModel + 0x18);
                if (!_packCardsSeen.Add(id)) return;

                string slug = Memory.Text(Memory.Ptr(viewModel + 0x20), 64);
                int songs = 0;
                IntPtr songArray = Memory.Ptr(viewModel + 0x38);
                if (Memory.LooksLikeObject(songArray)) songs = Memory.I32(songArray + Offsets.Runtime.ArrayLength);

                Diagnostics.Info($"pack card id={id} slug='{slug}' index={index} " +
                                 $"songs={songs} flags={Memory.U8(viewModel + 0x44)}/" +
                                 $"{Memory.U8(viewModel + 0x45)} tdb={Memory.U8(viewModel + 0x48)}");
            }
            catch (Exception e)
            {
                Diagnostics.Warn("pack card probe raised: " + Diagnostics.Describe(e));
            }
        }
    }
}

