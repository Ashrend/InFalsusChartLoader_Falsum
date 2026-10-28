using System;

namespace InFalsusChartLoader
{
    /// <summary>Room for custom files in the game's streaming-asset manager.</summary>
    internal static unsafe class StreamingAssets
    {
        private static int FieldFiles = Offsets.Unresolved;
        private static int FieldLengths = Offsets.Unresolved;  // long[] _uxA
        private static int FieldFlags = Offsets.Unresolved;    // bool[] _UxA
        private static int FieldChunks = Offsets.Unresolved;   // _DG[] _vxA

        /// <summary>`_BG._RxA` — the singleton.</summary>
        private static int SingletonField = Offsets.Unresolved;

        private static bool _fieldsAsked;

        private static bool _fieldsMissing;

        /// <summary>No stand-in value.</summary>
        private const int None = -1;

        private static IntPtr _bg;

        /// <summary>Where the next added file goes. Starts past everything the game shipped.</summary>
        private static int _next;

        /// <summary>File slots the model has let go of and a later audio can take.</summary>
        private static readonly Stack<int> Free = new Stack<int>();

        /// <summary>The first slot this mod allocated; the game's own files sit below it.</summary>
        private static int _first = None;

        internal static bool Resolved => _bg != IntPtr.Zero;

        /// <summary>Finds the manager and the first free index.</summary>
        internal static bool Resolve()
        {
            try
            {
                IntPtr klass = FieldResolver.ClassPointer("_BG");
                if (klass == IntPtr.Zero)
                {
                    Diagnostics.Warn("_BG could not be found by name; custom audio will not work");
                    return false;
                }

                IntPtr statics = FieldResolver.Statics(klass);
                if (statics == IntPtr.Zero)
                {
                    Diagnostics.Warn("_BG has no statics block; custom audio will not work");
                    return false;
                }

                if (!_fieldsAsked)
                {
                    _fieldsAsked = true;

                    FieldFiles = FieldResolver.Field("_BG", "_TxA");
                    FieldLengths = FieldResolver.Field("_BG", "_uxA");
                    FieldFlags = FieldResolver.Field("_BG", "_UxA");
                    FieldChunks = FieldResolver.Field("_BG", "_vxA");
                    SingletonField = FieldResolver.Field("_BG", "_RxA");

                    _fieldsMissing = FieldFiles < 0 || FieldLengths < 0 || FieldFlags < 0 ||
                                     FieldChunks < 0 || SingletonField < 0;
                }

                if (_fieldsMissing) return false;

                IntPtr bg = Memory.Ptr(statics + SingletonField);
                if (!Memory.LooksLikeObject(bg))
                {
                    Diagnostics.Warn("the streaming asset manager singleton is not there yet; " +
                                     "custom audio will not work");
                    return false;
                }

                IntPtr lengths = Memory.Ptr(bg + FieldLengths);
                if (!Memory.LooksLikeObject(lengths))
                {
                    Diagnostics.Warn("the streaming asset manager has no length table; custom audio " +
                                     "will not work");
                    return false;
                }

                int count = Memory.I32(lengths + Offsets.Runtime.ArrayLength);
                if (count <= 0)
                {
                    Diagnostics.Warn($"the streaming asset manager lists {count} files; custom audio " +
                                     "will not work");
                    return false;
                }

                _bg = bg;
                _next = count;
                if (_first < 0) _first = count;

                Diagnostics.Info($"streaming assets: {count} files, custom files start at {_first}");
                return true;
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the streaming asset manager could not be reached: {Diagnostics.Describe(e)}");
                return false;
            }
        }

        /// <summary>Makes room for one more file and records its length.</summary>
        internal static int Add(int length)
        {
            if (!Resolved) return -1;

            if (Free.Count > 0)
            {
                int reused = Free.Pop();
                if (SetLength(reused, length)) return reused;

                Diagnostics.Warn($"audio slot {reused} could not take a new length");
                return -1;
            }

            int index = _next;
            int size = index + 1;

            if (!Grow(FieldFiles, size, standIn: 0, own: false)) return -1;
            if (!Grow(FieldFlags, size, standIn: None, own: false)) return -1;
            if (!Grow(FieldChunks, size, standIn: 8, own: true)) return -1;
            if (!Grow(FieldLengths, size, standIn: None, own: false)) return -1;

            IntPtr lengths = Memory.Ptr(_bg + FieldLengths);
            Memory.WriteI64(lengths + Offsets.Runtime.ArrayDataOffset + index * sizeof(long), length);

            _next = size;
            return index;
        }

        /// <summary>Takes a file slot back for a later audio.</summary>
        internal static void Release(int index)
        {
            if (!Resolved || index < _first)
            {
                Diagnostics.Warn($"audio slot {index} is not one this mod can reuse");
                return;
            }

            Free.Push(index);
        }

        /// <summary>Rewrites the length the manager holds for a file it already lists.</summary>
        internal static bool SetLength(int index, long length)
        {
            if (!Resolved || index < 0) return false;

            IntPtr lengths = Memory.Ptr(_bg + FieldLengths);
            if (!Memory.LooksLikeObject(lengths)) return false;

            int count = Memory.I32(lengths + Offsets.Runtime.ArrayLength);
            if (index >= count) return false;

            Memory.WriteI64(lengths + Offsets.Runtime.ArrayDataOffset + index * sizeof(long), length);
            return true;
        }

        /// <summary>Replaces one of the manager's arrays with a larger one.</summary>
        private static bool Grow(int field, int size, int standIn, bool own)
        {
            IntPtr old = Memory.Ptr(_bg + field);
            if (!Memory.LooksLikeObject(old)) { Diagnostics.Warn($"field +0x{field:X} is not an array"); return false; }

            int oldCount = Memory.I32(old + Offsets.Runtime.ArrayLength);
            if (size <= oldCount) return true;

            int stride = oldCount > 0
                ? (int)(Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(old) / oldCount)
                : sizeof(long);
            if (stride <= 0) return false;
            if (standIn != None && standIn + IntPtr.Size > stride)
            {
                Diagnostics.Warn($"field +0x{field:X} has {stride}-byte elements with no room at +0x{standIn:X}");
                return false;
            }

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(old);
            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element == IntPtr.Zero) { Diagnostics.Warn($"field +0x{field:X} has no element class"); return false; }

            IntPtr grown = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, (ulong)size);
            if (grown == IntPtr.Zero) return false;

            IntPtr source = old + Offsets.Runtime.ArrayDataOffset;
            IntPtr target = grown + Offsets.Runtime.ArrayDataOffset;
            Buffer.MemoryCopy((void*)source, (void*)target, (long)size * stride, (long)oldCount * stride);

            if (standIn != None && size > oldCount)
            {
                IntPtr stand = IntPtr.Zero;
                for (int i = 0; i < oldCount && stand == IntPtr.Zero; i++)
                {
                    IntPtr value = *(IntPtr*)(source + i * stride + standIn);
                    if (!Memory.LooksLikeObject(value)) continue;
                    stand = own ? EmptyLike(value) : value;
                }

                if (stand == IntPtr.Zero)
                {
                    Diagnostics.Warn($"no entry of +0x{field:X} has anything at +0x{standIn:X} to go " +
                                     "by; the custom files are not registered");
                    return false;
                }

                for (int i = oldCount; i < size; i++)
                    *(IntPtr*)(target + i * stride + standIn) = stand;
            }

            Memory.WritePtr(_bg + field, grown);
            return true;
        }

        /// <summary>A fresh empty instance of the same class as <paramref name="like"/>.</summary>
        private static IntPtr EmptyLike(IntPtr like)
        {
            if (!Memory.LooksLikeObject(like)) return IntPtr.Zero;

            IntPtr klass = Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(like);
            if (klass == IntPtr.Zero) return IntPtr.Zero;

            IntPtr element = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_element_class(klass);
            if (element != IntPtr.Zero)
                return Il2CppInterop.Runtime.IL2CPP.il2cpp_array_new(element, 0);

            Il2CppInterop.Runtime.IL2CPP.il2cpp_runtime_class_init(klass);
            return Il2CppInterop.Runtime.IL2CPP.il2cpp_object_new(klass);
        }
    }
}
