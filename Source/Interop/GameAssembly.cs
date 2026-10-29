using System;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The game's native module; its base address is reported by the probe.</summary>
    internal static class GameAssembly
    {
        /// <summary>The module the game's native code is in.</summary>
        private const string Module = "GameAssembly.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        private static IntPtr _base;

        /// <summary>Base address GameAssembly.dll is loaded at, or Zero if it is not loaded.</summary>
        internal static IntPtr Base
        {
            get
            {
                if (_base == IntPtr.Zero) _base = GetModuleHandleW(Module);
                return _base;
            }
        }
    }
}
