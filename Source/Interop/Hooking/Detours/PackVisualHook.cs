using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    internal static unsafe partial class Hooks
    {
        private const string PackImage = "Game.dll";
        private const string PackNamespace = "ifapp.Game";

        private static int CardsField = Offsets.Unresolved;
        private static int CardViewModel = Offsets.Unresolved;
        private static int PackDataAccess = Offsets.Unresolved;
        private static int ViewModelSong = Offsets.Unresolved;
        private static int RowLargeCard = Offsets.Unresolved;
        private static int RowSmallCard = Offsets.Unresolved;
        private static int ViewModelDifficulty = Offsets.Unresolved;
        private static bool _fieldsResolved;
        private static bool _fieldsMissing;

        private static bool ResolveFields()
        {
            if (_fieldsResolved) return true;
            if (_fieldsMissing) return false;

            PackDataAccess = FieldResolver.Field("PackVisualMemberLarge", "dataAccess");
            ViewModelSong = FieldResolver.Field("_SH", "_WEb");
            CardsField = FieldResolver.Field("PackVisualMemberLarge", "_Mf");
            CardViewModel = FieldResolver.Field("PackSongCardMember", "_if");
            ViewModelDifficulty = FieldResolver.Field("_SH", "_xEb");
            RowLargeCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectLargeNonCompleted");
            RowSmallCard = FieldResolver.Field("SongSelectPackAssets", "PackSelectNonCompleted");

            if (PackDataAccess < 0 || ViewModelSong < 0 || CardsField < 0 ||
                CardViewModel < 0 || ViewModelDifficulty < 0 ||
                RowLargeCard < 0 || RowSmallCard < 0)
            {
                _fieldsMissing = true;
                Diagnostics.Error("the pack's card fields could not be read by name in this build; the " +
                                  "cards will keep the difficulty they were first built with");
                return false;
            }

            _fieldsResolved = true;
            return true;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackDifficultyFn(IntPtr self, byte difficulty, IntPtr methodInfo);

        private static NativeHook<PackDifficultyFn> _packDifficulty;
        private static PackDifficultyFn _packDifficultyTramp;
        private static delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void> _buildCard;
        private static IntPtr _buildCardInfo;

        internal static long PackDifficultyCalls, PackCardsRebuilt;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void PackRowFn(IntPtr table, IntPtr destination, uint index);

        private static NativeHook<PackRowFn> _packRow;
        private static PackRowFn _packRowTramp;
        private static int _rowSource = -1;
        private static int _rowSize = -1;
        private static bool _rebuildReported;

        private static bool InstallPackRow()
        {
            int candidates = 0;
            IntPtr target = RowAccessor(ref candidates);
            if (target == IntPtr.Zero)
            {
                if (candidates < 0)
                {
                    Diagnostics.Error("the game's pack row getter could not be found by name in this " +
                                      "build; the custom pack will wear whatever this build puts in its row");
                    return false;
                }

                Diagnostics.Info($"the pack row accessor: the getter's most-called function is called " +
                                 $"{candidates} time(s); a row is {RowSize()} byte(s)");
                Diagnostics.Error("the pack row accessor could not be located in this build; the custom " +
                                  "pack will wear whatever this build puts in its row");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packRow = new NativeHook<PackRowFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, uint, void>)&PackRowDetour,
            };
            _packRow.Attach();
            _packRowTramp = _packRow.Trampoline;
            Diagnostics.Info($"the pack row accessor is at 0x{target.ToInt64():X}");
            return Landed(Hook.PackRow, target, prologue);
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackRowDetour(IntPtr table, IntPtr destination, uint index)
        {
#if DEBUG
            RowCalls++;
            long t0 = Now;
#endif
            if (SongCatalog.PacksById.TryGetValue((int)index, out ChartPack pack))
            {
                int row = pack.VisualRow;
                if (row < 0)
                {
                    if (_rowSource < 0) _rowSource = ResolveSourceRow();
                    row = _rowSource;
                }

                if (row >= 0)
                {
                    index = (uint)row;
#if DEBUG
                    RowRedirects++;
#endif
                }
            }
#if DEBUG
            TicksRow += Now - t0;
#endif

            _packRowTramp(table, destination, index);
        }

        private static int ResolveSourceRow()
        {
            if (!PackSetup.TryGetVisuals(out IntPtr canonical) || !Memory.LooksLikeObject(canonical))
                return -1;

            int offset = PackSetup.VisualsTableOffset;
            IntPtr table = offset > 0 ? Memory.Ptr(canonical + offset) : IntPtr.Zero;
            int count = Memory.LooksLikeObject(table) ? Memory.I32(table + Offsets.Runtime.ArrayLength) : 0;
            int stride = Stride(table, count);
            int row = stride <= 0 ? -1 : SourceRow(table, count, stride);
            if (row < 0) PackSetup.NoRow();
            return row;
        }

        private static IntPtr RowAccessor(ref int candidates)
        {
            IntPtr getter = MethodResolver.ByName("PackSelectSceneAssets", "_WnA");
            if (getter == IntPtr.Zero)
            {
                candidates = -1;
                return IntPtr.Zero;
            }

            byte* code = (byte*)getter;
            IntPtr best = IntPtr.Zero;
            int bestCount = 0;
            bool tied = false;

            for (int at = 0; at + CallLength <= CallWindow; at++)
            {
                if (code[at] != 0xE8) continue;

                IntPtr target = Target(code + at);
                if (!Executable(target)) continue;

                int count = 1;
                for (int other = at + CallLength; other + CallLength <= CallWindow; other++)
                    if (code[other] == 0xE8 && Target(code + other) == target)
                        count++;

                if (count > bestCount) { bestCount = count; best = target; tied = false; }
                else if (count == bestCount) tied = true;
            }

            candidates = bestCount;
            return bestCount > 1 && !tied ? best : IntPtr.Zero;
        }

        private const int CallLength = 5;
        private const int CallWindow = 96;

        private static IntPtr Target(byte* call) =>
            (IntPtr)((long)(call + CallLength) + *(int*)(call + 1));

        private static bool Executable(IntPtr address)
        {
            byte* image = (byte*)GameAssembly.Base;
            if (image == null || *(ushort*)image != 0x5A4D) return false;

            byte* pe = image + *(int*)(image + 0x3C);
            if (*(uint*)pe != 0x00004550) return false;

            int sections = *(ushort*)(pe + 6);
            byte* section = pe + 24 + *(ushort*)(pe + 20);
            long at = address.ToInt64();

            for (int s = 0; s < sections; s++, section += 40)
            {
                if ((*(uint*)(section + 36) & 0x20000000) == 0) continue;

                uint size = *(uint*)(section + 8);
                uint start = *(uint*)(section + 12);
                if (size == 0 || start == 0) continue;

                long from = (long)(image + start);
                if (at >= from && at < from + size) return true;
            }

            return false;
        }

        private static int RowSize()
        {
            if (_rowSize >= 0) return _rowSize;

            int size = FieldResolver.ValueSize("SongSelectPackAssets");
            if (size <= 0) return 0;

            return _rowSize = size;
        }

        internal static int SourceRow(IntPtr table, int count, int stride)
        {
            if (stride <= 0 || count <= 0) return -1;

            for (int i = 1; i < count; i++)
                if (!SongCatalog.CustomPackIds.Contains(i) && Drawable(table, count, stride, i)) return i;

            return -1;
        }

        private static int Stride(IntPtr table, int count)
        {
            if (!Memory.LooksLikeObject(table) || count <= 0) return 0;

            try
            {
                return (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(table) / count);
            }
            catch (Exception)
            {
                return 0;
            }
        }

        private static bool Drawable(IntPtr table, int count, int stride, int id)
        {
            if (!ResolveFields()) return false;

            IntPtr row = Row(table, count, stride, id);
            if (row == IntPtr.Zero) return false;

            return Memory.LooksLikeObject(Memory.Ptr(row + RowLargeCard))
                || Memory.LooksLikeObject(Memory.Ptr(row + RowSmallCard));
        }

        private static IntPtr Row(IntPtr table, int count, int stride, int id)
        {
            if (stride <= 0 || count <= 0) return IntPtr.Zero;
            if (id < 0 || id >= count) id = 0;

            return table + Offsets.Runtime.ArrayDataOffset + id * stride;
        }

        private static bool InstallPackVisual()
        {
            bool row = InstallPackRow();

            IntPtr info = MethodResolver.MethodInfoByRuntime(
                "PackSongCardMember", "_pc", PackImage, PackNamespace);
            if (info == IntPtr.Zero)
            {
                Diagnostics.Warn("PackSongCardMember._pc could not be found; a difficulty change on the " +
                                 "pack screen will not reach the jackets until the screen is reloaded");
            }
            else
            {
                _buildCardInfo = info;
                _buildCard = (delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, IntPtr, void>)(*(IntPtr*)info);
            }

            IntPtr target = MethodResolver.ByRuntime(
                "PackVisualMemberLarge", "_vc", PackImage, PackNamespace);
            if (target == IntPtr.Zero)
            {
                Diagnostics.Warn("PackVisualMemberLarge._vc could not be found; the jackets on the pack " +
                                 "screen will keep the difficulty they were first built with");
                return false;
            }

            byte[] prologue = Prologue(target);
            _packDifficulty = new NativeHook<PackDifficultyFn>
            {
                Target = target,
                Detour = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte, IntPtr, void>)&PackDifficultyDetour,
            };
            _packDifficulty.Attach();
            _packDifficultyTramp = _packDifficulty.Trampoline;
            Diagnostics.Info("PackVisualMemberLarge._vc hooked");
            return Landed(Hook.PackVisual, target, prologue) & row;
        }

        private static void DetachPackVisual()
        {
            _packRow?.Detach();
            _packRow = null;
            _packRowTramp = null;

            _packDifficulty?.Detach();
            _packDifficulty = null;
            _packDifficultyTramp = null;
            _buildCard = null;
            _buildCardInfo = IntPtr.Zero;
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static void PackDifficultyDetour(IntPtr self, byte difficulty, IntPtr methodInfo)
        {
            PackDifficultyCalls++;

            bool rebuild = false;
            if (!Faulted)
            {
#if DEBUG
                long t0 = Now;
#endif
                try
                {
                    Selection.ApplyingDifficulty(difficulty);
                    rebuild = CardsForAnotherDifficulty(self, difficulty);
                }
                catch (Exception e)
                {
                    Fault(Hook.PackVisual, e);
                }
#if DEBUG
                TicksVc += Now - t0;
#endif
            }

            _packDifficultyTramp(self, difficulty, methodInfo);

            if (rebuild)
            {
#if DEBUG
                long t1 = Now;
#endif
                RebuildCards(self);
#if DEBUG
                TicksVc += Now - t1;
#endif
            }
        }

        private static bool CardsForAnotherDifficulty(IntPtr packVisual, byte difficulty)
        {
            if (!ResolveFields()) return false;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return false;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr _, out IntPtr viewModel)) continue;
                if (Memory.U8(viewModel + ViewModelDifficulty) != difficulty) return true;
            }

            return false;
        }

        private static void RebuildCards(IntPtr packVisual)
        {
            if (_buildCard == null) return;

            if (!ResolveFields()) return;

            IntPtr dataAccess = Memory.Ptr(packVisual + PackDataAccess);
            if (!Memory.LooksLikeObject(dataAccess)) return;

            if (!Memory.TryList(Memory.Ptr(packVisual + CardsField), out IntPtr items, out int count))
                return;

            for (int i = 0; i < count; i++)
            {
                if (!Ours(items, i, out IntPtr card, out IntPtr viewModel)) continue;

                try
                {
                    _buildCard(card, dataAccess, viewModel, _buildCardInfo);
                    PackCardsRebuilt++;
                }
                catch (Exception e)
                {
                    Reported("rebuilding a card on the pack screen raised: " + Diagnostics.Describe(e));
                }
            }
        }

        private static bool Ours(IntPtr items, int index, out IntPtr card, out IntPtr viewModel)
        {
            card = IntPtr.Zero;
            viewModel = IntPtr.Zero;

            if (!ResolveFields()) return false;

            card = Memory.Ptr(items + index * IntPtr.Size);
            if (!Memory.LooksLikeObject(card)) return false;

            viewModel = Memory.Ptr(card + CardViewModel);
            if (!Memory.LooksLikeObject(viewModel)) return false;

            return JacketCatalog.IsOurs(viewModel + ViewModelSong);
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void Reported(string why)
        {
            if (_rebuildReported) return;
            _rebuildReported = true;
            Diagnostics.Warn(why);
        }
    }
}
