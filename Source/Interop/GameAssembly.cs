using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The game's native module; its base address is reported by the probe.</summary>
    internal static class GameAssembly
    {
        /// <summary>The module the game's native code is in.</summary>
        private const string Module = "GameAssembly.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern IntPtr GetModuleHandleW(string moduleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = false)]
        private static extern int GetModuleFileNameW(IntPtr module, StringBuilder fileName, int size);

        private static IntPtr _base;

        /// <summary>The image size, once it has been read; <see cref="long.MinValue"/> means "not yet".</summary>
        private static long _imageSize = long.MinValue;

        /// <summary>The module's path; null means "not yet", an empty string means "looked, and there is none".</summary>
        private static string _path;

        /// <summary>The build identity; null means "not yet", an empty string means "looked, and there is none".</summary>
        private static string _identity;

        /// <summary>Base address GameAssembly.dll is loaded at, or Zero if it is not loaded.</summary>
        internal static IntPtr Base
        {
            get
            {
                if (_base == IntPtr.Zero) _base = GetModuleHandleW(Module);
                return _base;
            }
        }

        /// <summary>How big the loaded image is, or zero when it cannot be read.</summary>
        /// <remarks>
        /// Read out of the module's own PE header rather than through GetModuleInformation:
        /// that call's struct has two incompatible layouts in the Windows headers, and
        /// getting it wrong silently returns a wrong size.
        /// </remarks>
        internal static long SizeOfImage
        {
            get
            {
                if (_imageSize != long.MinValue) return _imageSize;

                // A module that is not loaded yet is not an answer, so it is not remembered
                // either: the size has to stay askable for later.
                IntPtr module = Base;
                if (module == IntPtr.Zero) return 0;

                _imageSize = ImageSizeAt(module);
                return _imageSize;
            }
        }

        /// <summary>The size of the PE image loaded at <paramref name="image"/>, or zero.</summary>
        /// <remarks>
        /// This is the testable core of <see cref="SizeOfImage"/>. The offset arithmetic below
        /// is the guard that keeps the address cache from recording a target outside the
        /// module — a wrong value here would let a detour land on the wrong function, so it
        /// is worth a check of its own.
        /// </remarks>
        internal static long ImageSizeAt(IntPtr image)
        {
            if (image == IntPtr.Zero) return 0;

            try
            {
                // 'MZ' is the two DOS-magic bytes. It has to be read as a word: the two bytes
                // behind it belong to the next header field, so a dword read never equals the
                // magic and every real image would be refused.
                if (Marshal.ReadInt16(image) != 0x5A4D) return 0;

                int pe = Marshal.ReadInt32(image, 0x3C);
                if (pe <= 0) return 0;
                if (Marshal.ReadInt32(image, pe) != 0x00004550) return 0;           // 'PE'

                return (uint)Marshal.ReadInt32(image, pe + 0x50);
            }
            catch (AccessViolationException)
            {
                // An address that cannot be read holds no image.
                return 0;
            }
        }

        /// <summary>The loaded module's file path, or null when it cannot be read.</summary>
        /// <remarks>
        /// The path is asked for by module handle, and the handle has to name a module: a
        /// null handle would make the call answer with the host program's own path instead,
        /// which is the wrong file to build a cache key out of.
        /// </remarks>
        internal static string Path
        {
            get
            {
                if (_path != null) return _path.Length == 0 ? null : _path;

                // A module that is not loaded yet is not an answer, so it is not remembered
                // either: the path has to stay askable for later.
                IntPtr module = Base;
                if (module == IntPtr.Zero) return null;

                var buffer = new StringBuilder(1024);
                int written = GetModuleFileNameW(module, buffer, buffer.Capacity);
                _path = written > 0 ? buffer.ToString() : string.Empty;
                return _path.Length == 0 ? null : _path;
            }
        }

        /// <summary>What tells one game build from another, or null when it cannot be read.</summary>
        /// <remarks>
        /// The form is "&lt;file size&gt;-&lt;last write ticks&gt;": a game update moves the file,
        /// and a moved file changes the key, which is what throws the old cache away.
        /// </remarks>
        internal static string Identity
        {
            get
            {
                if (_identity != null) return _identity.Length == 0 ? null : _identity;

                // Nothing to key on while the module is not there, so nothing is remembered:
                // the identity has to stay askable for later.
                string path = Path;
                if (path == null) return null;

                _identity = string.Empty;
                try
                {
                    var file = new FileInfo(path);
                    _identity = $"{file.Length}-{file.LastWriteTimeUtc.Ticks}";
                }
                catch (Exception)
                {
                    _identity = string.Empty;
                }

                return _identity.Length == 0 ? null : _identity;
            }
        }
    }
}
