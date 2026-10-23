using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader
{
    internal static unsafe partial class Hooks
    {
        /// <summary>`PackSelectScene.Awake` — the game builds its pack list, entries and card windows.</summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackListFn(IntPtr self, IntPtr methodInfo);

        private static NativeHook<PackListFn> _packListHook;
        private static PackListFn _packListTramp;

        /// <summary>Pack screens built.</summary>
        internal static long PackListsBuilt;

        /// <summary>The one sentence a build reports when the empty pack cannot be left out.</summary>
        internal const string EmptyPackBuildFailed =
            "the empty custom pack could not be left out of the category list; it will appear until the scene is entered again";

        private static bool InstallPackList()
        {
            IntPtr info = MethodResolver.MethodInfoByRuntime("PackSelectScene", "Awake", "Game.dll", "ifapp.Game");
            if (info == IntPtr.Zero || *(IntPtr*)info == IntPtr.Zero ||
                Il2CppInterop.Runtime.IL2CPP.il2cpp_method_get_param_count(info) != 0)
            {
                Diagnostics.Error(SongSelectRefresh.PackListUnavailable);
                return false;
            }

            IntPtr target = *(IntPtr*)info;
            byte[] prologue = Prologue(target);

            _packListHook = new NativeHook<PackListFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, void>)&PackListDetour,
            };
            _packListHook.Attach();
            _packListTramp = _packListHook.Trampoline;
            Diagnostics.Info("PackSelectScene.Awake hooked");
            PackScreenRebuild.Prepare(info);
            return Landed(Hook.PackList, target, prologue);
        }

        private static void DetachPackList()
        {
            _packListHook?.Detach();
            _packListHook = null;
            _packListTramp = null;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackListDetour(IntPtr self, IntPtr methodInfo)
        {
            if (PackScreenRebuild.InProgress)
            {
                _packListTramp(self, methodInfo);
                PackListsBuilt++;
                if (!Faulted) PackScreenRebuild.SnapScrolls(self);
                return;
            }

            IntPtr table = IntPtr.Zero;
            bool hidden = false;

            if (!Faulted)
            {
                try
                {
                    bool empty = SongCatalog.EmptyPackListed();
                    table = SongCatalog.HideEmptyPackForBuild();
                    hidden = table != IntPtr.Zero;

                    if (empty && !hidden) Diagnostics.Error(EmptyPackBuildFailed);
                    if (hidden) SongSelectRefresh.DeselectIfCustomPackSelected();
                }
                catch (Exception e)
                {
                    Fault(Hook.PackList, e);
                }
            }

            try
            {
                _packListTramp(self, methodInfo);
                PackListsBuilt++;
            }
            finally
            {
                if (hidden)
                {
                    try { SongCatalog.RestorePackTable(table); }
                    catch (Exception e) { Fault(Hook.PackList, e); }
                }
            }

            if (!Faulted) SongSelectRefresh.NotePackScreenBuilt(hidden);
            if (!Faulted) PackScreenRebuild.SnapScrolls(self);
        }
    }
}
