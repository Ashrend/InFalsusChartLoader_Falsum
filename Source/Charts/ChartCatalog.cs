using System;
using System.Collections.Generic;
using System.IO;

namespace InFalsusChartLoader
{
    /// <summary>The custom charts, keyed by the name the game requests them under.</summary>
    internal static unsafe class ChartCatalog
    {
        private const string Extension = ".spc";

        /// <summary>Folders that imported, by base name. Ordinal: these are file names.</summary>
        private static readonly Dictionary<string, ChartInfo> ByBase =
            new Dictionary<string, ChartInfo>(StringComparer.Ordinal);

        /// <summary>Registers a chart under the name the game will request.</summary>
        internal static void Adopt(ChartInfo info) => ByBase[info.BaseName] = info;

        /// <summary>Answers a chart request; false when the name is not this mod's.</summary>
        internal static bool TryServe(IntPtr namePtr, IntPtr result)
        {
            string name = Memory.Text(namePtr, 128);
            if (name == null) return false;
            if (!Split(name, out string baseName, out int difficulty)) return false;

            if (!ByBase.TryGetValue(baseName, out ChartInfo info))
            {
                Diagnostics.Info($"chart '{name}' is not ours; passing through");
                return false;
            }

            string path = info.ChartPaths[difficulty];
            string seed = info.ChartNames[difficulty];

            bool decoded = false;
            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                decoded = ChartCodec.TryDecode(bytes, seed, result, out string reason);
                if (!decoded) Diagnostics.Error($"'{info.Name}' difficulty {difficulty}: {reason}");
            }
            catch (Exception e)
            {
                Diagnostics.Error($"'{info.Name}' difficulty {difficulty} could not be read: " +
                                  Diagnostics.Describe(e));
            }

            if (!decoded && !ChartCodec.Empty(result)) return false;

            int sourceText = SourceTextField();
            if (sourceText >= 0) *(IntPtr*)(result + sourceText) = IntPtr.Zero;
            return true;
        }

        private static int _sourceTextField = int.MinValue;

        /// <summary>The offset of the field <see cref="TryServe"/> clears.</summary>
        private static int SourceTextField()
        {
            if (_sourceTextField == int.MinValue) _sourceTextField = FieldResolver.Field("_R", "_RC");
            return _sourceTextField;
        }

        /// <summary>Splits a chart name into its base name and difficulty.</summary>
        private static bool Split(string name, out string baseName, out int difficulty)
        {
            baseName = null;
            difficulty = 0;

            if (!name.EndsWith(Extension, StringComparison.Ordinal)) return false;

            int end = name.Length - Extension.Length;
            if (end < 2) return false;

            char last = name[end - 1];
            if (last < '0' || last > '3') return false;

            difficulty = last - '0';
            baseName = name.Substring(0, end - 1);
            return true;
        }
    }
}
