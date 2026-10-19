using System;
using System.Collections.Generic;
using System.Reflection;
#if BEPINEX
using System.IO;
#endif

namespace InFalsusChartLoader
{
    /// <summary>The generated interop types, indexed by name.</summary>
    internal static class InteropTypeIndex
    {
        private static Dictionary<string, List<Type>> _byName;

        /// <summary>Generated types by simple name. A name can hit more than one type.</summary>
        internal static Dictionary<string, List<Type>> ByName()
        {
            if (_byName != null) return _byName;

            var index = new Dictionary<string, List<Type>>(StringComparer.Ordinal);
            int scanned = 0;

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsGenerated(asm)) continue;

                Type[] types;
                try
                {
                    types = asm.GetTypes();
                }
                catch (ReflectionTypeLoadException e)
                {
                    types = e.Types;
                }
                catch (Exception e)
                {
                    Diagnostics.Warn($"{asm.GetName().Name} was skipped: {Diagnostics.Describe(e)}");
                    continue;
                }

                foreach (Type t in types)
                {
                    if (t == null) continue;

                    try
                    {
                        scanned++;

                        Add(index, t.Name, t);

                        if (t.DeclaringType != null) Add(index, t.DeclaringType.Name + "." + t.Name, t);
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            Diagnostics.Info($"indexed {scanned} interop types by name");
            return _byName = index;
        }

        /// <summary>Whether an assembly is one of the game's generated interop assemblies.</summary>
        private static bool IsGenerated(Assembly assembly)
        {
            string name = assembly.GetName().Name;
            if (name == null) return false;
            if (name.StartsWith("Il2Cpp", StringComparison.Ordinal)) return true;

#if BEPINEX
            if (name.StartsWith("Unity", StringComparison.Ordinal)) return false;
            return InteropAssemblyNames().Contains(name);
#else
            return false;
#endif
        }

#if BEPINEX
        private static HashSet<string> _interopNames;

        /// <summary>The assembly names in BepInEx's interop folder.</summary>
        private static HashSet<string> InteropAssemblyNames()
        {
            if (_interopNames != null) return _interopNames;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                string folder = Path.Combine(BepInEx.Paths.BepInExRootPath, "interop");
                foreach (string path in Directory.GetFiles(folder, "*.dll"))
                    names.Add(Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception e)
            {
                Diagnostics.Warn($"the interop folder could not be read: {Diagnostics.Describe(e)}");
            }

            return _interopNames = names;
        }
#endif

        private static void Add(Dictionary<string, List<Type>> index, string key, Type type)
        {
            if (!index.TryGetValue(key, out List<Type> bucket))
                index[key] = bucket = new List<Type>(1);
            bucket.Add(type);
        }
    }
}
