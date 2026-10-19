using System;
using System.Collections.Generic;
using System.Reflection;

namespace InFalsusChartLoader
{
    /// <summary>Resolves a game field's offset by name.</summary>
    internal static class FieldResolver
    {
        private const BindingFlags AnyField =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

        /// <summary>The class and monitor pointers every IL2CPP object starts with.</summary>
        private const int ObjectHeader = Offsets.Runtime.ObjectHeader;

        private static int _asked;
        private static int _unresolved;

        /// <summary>The line a Release build carries: how many names this mod reads by were answered.</summary>
        internal static string Stats() =>
            $"offsets asked={_asked} unresolved={_unresolved}";

        /// <summary>A field's offset by name, or -1 when the name cannot be resolved.</summary>
        internal static int Field(string typeName, string fieldName)
        {
            if (!TryField(typeName, fieldName, out int offset))
            {
                _unresolved++;
                Diagnostics.Error($"{typeName}.{fieldName}: no field by that name in this build");
                return -1;
            }

            _asked++;
            return offset;
        }

        /// <summary>A field's offset by name, or -1 when it may not exist.</summary>
        internal static int Lookup(string typeName, string fieldName) =>
            TryField(typeName, fieldName, out int offset) ? offset : -1;

        /// <summary>An enum member's value by name, or -1 when it cannot be resolved.</summary>
        internal static int EnumValue(string typeName, string memberName)
        {
            try
            {
                if (InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                {
                    foreach (Type type in types)
                    {
                        if (type == null || !type.IsEnum) continue;

                        FieldInfo member = type.GetField(memberName,
                                                          BindingFlags.Public | BindingFlags.Static);
                        if (member == null) continue;

                        object raw = member.GetRawConstantValue();
                        if (raw == null) continue;

                        _asked++;
                        return Convert.ToInt32(raw);
                    }
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{typeName}.{memberName} could not be read: {Diagnostics.Describe(e)}");
            }

            _unresolved++;
            Diagnostics.Error($"{typeName}.{memberName}: no enum member by that name in this build");
            return -1;
        }

        // ---------------------------------------------------------------- sizes

        private static readonly Dictionary<string, int> Measured = new Dictionary<string, int>();

        /// <summary>The element size of an IL2CPP array, or -1 when it cannot be read.</summary>
        internal static int ElementSize(IntPtr array, string label)
        {
            if (Measured.TryGetValue(label, out int known)) return known;

            string why = null;
            try
            {
                if (Memory.LooksLikeObject(array))
                {
                    uint length = Il2CppInterop.Runtime.IL2CPP.il2cpp_array_length(array);
                    uint bytes = length == 0
                        ? 0
                        : Il2CppInterop.Runtime.IL2CPP.il2cpp_array_get_byte_length(array);

                    if (length > 0 && length <= 1_000_000 && bytes > 0 && bytes % length == 0)
                    {
                        int size = (int)(bytes / length);
                        Measured[label] = size;
                        _asked++;
                        return size;
                    }

                    why = $"the array holds {length} element(s) in {bytes} byte(s)";
                }
                else
                {
                    why = "there is no array to measure";
                }
            }
            catch (Exception e)
            {
                why = Diagnostics.Describe(e);
            }

            _unresolved++;
            Diagnostics.Error($"{label} could not be measured off the live arrays in this build: {why}");
            return -1;
        }

        /// <summary>The class pointer for a generated type.</summary>
        internal static IntPtr ClassPointer(string typeName)
        {
            if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                return IntPtr.Zero;

            foreach (Type type in types)
            {
                try
                {
                    FieldInfo pointer = typeof(Il2CppInterop.Runtime.Il2CppClassPointerStore<>)
                        .MakeGenericType(type)
                        .GetField("NativeClassPtr", BindingFlags.Public | BindingFlags.Static);

                    if (pointer == null) continue;

                    IntPtr klass = (IntPtr)pointer.GetValue(null);
                    if (klass != IntPtr.Zero) return klass;
                }
                catch (Exception e)
                {
                    Diagnostics.Warn($"{typeName} class pointer could not be read: {Diagnostics.Describe(e)}");
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>A class's statics block.</summary>
        internal static IntPtr Statics(IntPtr klass)
        {
            if (!Memory.LooksLikeObject(klass)) return IntPtr.Zero;
            return Memory.Ptr(klass + Offsets.Runtime.ClassStatics);
        }

        /// <summary>The size of a value type, or -1 when it cannot be resolved.</summary>
        internal static int ValueSize(string typeName)
        {
            IntPtr klass = ClassPointer(typeName);
            if (klass == IntPtr.Zero || !IsValueType(klass)) return -1;

            long boxed = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_instance_size(klass);
            return boxed > ObjectHeader ? (int)(boxed - ObjectHeader) : -1;
        }

        private static bool TryField(string typeName, string fieldName, out int offset)
        {
            offset = 0;

            try
            {
                if (!InteropTypeIndex.ByName().TryGetValue(typeName, out List<Type> types))
                    return false;

                foreach (Type type in types)
                {
                    FieldInfo pointer = PointerField(type, fieldName);
                    if (pointer == null) continue;

                    IntPtr info = (IntPtr)pointer.GetValue(null);
                    if (info == IntPtr.Zero) continue;

                    offset = (int)Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(info) -
                             (IsValueType(ClassOf(type)) ? ObjectHeader : 0);
                    return true;
                }
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"{typeName}.{fieldName} could not be resolved: {Diagnostics.Describe(e)}");
            }

            return false;
        }

        /// <summary>The IL2CPP class of a generated <see cref="Type"/>.</summary>
        private static IntPtr ClassOf(Type type)
        {
            try
            {
                FieldInfo pointer = typeof(Il2CppInterop.Runtime.Il2CppClassPointerStore<>)
                    .MakeGenericType(type)
                    .GetField("NativeClassPtr", BindingFlags.Public | BindingFlags.Static);

                return pointer == null ? IntPtr.Zero : (IntPtr)pointer.GetValue(null);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Whether an IL2CPP class is a value type.</summary>
        private static bool IsValueType(IntPtr klass)
        {
            IntPtr parent = Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_parent(klass);
            if (parent == IntPtr.Zero) return false;

            return Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_name_(parent) == "ValueType";
        }

        /// <summary>The generated `NativeFieldInfoPtr_` static for a field.</summary>
        private static FieldInfo PointerField(Type type, string fieldName)
        {
            for (Type t = type; t != null; t = t.BaseType)
            {
                FieldInfo f = t.GetField("NativeFieldInfoPtr_" + fieldName, AnyField | BindingFlags.DeclaredOnly);
                if (f != null) return f;
            }

            return null;
        }
    }
}
