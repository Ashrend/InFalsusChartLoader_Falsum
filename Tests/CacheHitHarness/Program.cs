using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    /// <summary>A stand-in for the mod's entry point, which the harness does not link.</summary>
    /// <remarks>
    /// The address cache builds its key out of the mod's version, and the real
    /// <c>ModCore</c> brings the whole game in with it. Only the version constant is needed
    /// here, and it is the same in both sessions, which is what makes the cache key match.
    /// </remarks>
    internal static class ModCore
    {
        /// <summary>The mod's version, as the real one has it.</summary>
        internal const string Version = "0.0.0-harness";
    }
}

namespace CacheHitHarness
{
    using InFalsusChartLoader_falsum;

    /// <summary>
    /// Proves the address cache's hit path outside the game: it loads a module called
    /// <c>GameAssembly.dll</c> after the first lookup, so the cache has a real image to key
    /// on, and then asks for the same target again.
    /// </summary>
    internal static class Harness
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string path);

        /// <summary>The target the harness asks for; its identity is the same in both sessions.</summary>
        private const string Target = "harness:x";

        /// <summary>
        /// The offset the fake target sits at. It is inside the module's PE headers, which
        /// are the same bytes in every run, so the 16-byte fingerprint matches.
        /// </summary>
        private const long TargetRva = 0x100;

        private static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : "drive";
            string fake = Path.Combine(AppContext.BaseDirectory, "fake", "GameAssembly.dll");

            if (mode == "drive") return Drive(fake);
            return Session(fake, mode);
        }

        /// <summary>Runs the two sessions and puts the cache file back as it was found.</summary>
        private static int Drive(string fake)
        {
            // The fake module has to keep its identity between the two sessions: the cache is
            // keyed on the module's file size and last write time. So it is copied once and
            // then left alone.
            string folder = Path.GetDirectoryName(fake);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

            if (!File.Exists(fake))
            {
                File.Copy(FakeSource(), fake);
                Console.WriteLine("made the fake module: " + fake);
            }
            else
            {
                Console.WriteLine("reusing the fake module: " + fake);
            }

            // The mod's own cache file is left exactly as it was found.
            string cache = AddressCache.Path;
            bool existed = File.Exists(cache);
            byte[] saved = existed ? File.ReadAllBytes(cache) : null;
            if (existed) File.Delete(cache);

            int first;
            int second;
            try
            {
                Console.WriteLine();
                Console.WriteLine("=== session 1: the cache is written ===");
                first = Run(fake, "write");
                Show(cache);

                Console.WriteLine();
                Console.WriteLine("=== session 2: the cache is read back ===");
                second = Run(fake, "hit");
                Show(cache);
            }
            finally
            {
                if (existed)
                {
                    File.WriteAllBytes(cache, saved);
                    Console.WriteLine();
                    Console.WriteLine("restored the cache file that was there before");
                }
                else if (File.Exists(cache))
                {
                    File.Delete(cache);
                    Console.WriteLine();
                    Console.WriteLine("removed the cache file this run wrote");
                }
            }

            bool ok = first == 0 && second == 0;
            Console.WriteLine();
            Console.WriteLine(ok
                ? "PASS: the cache was written, and the next run answered out of it."
                : "FAIL: see the sessions above.");
            return ok ? 0 : 1;
        }

        /// <summary>One session: the cache is asked before the module is there, and again after.</summary>
        private static int Session(string fake, string mode)
        {
            // 1. The cache is asked while there is no game module: it must stay inert.
            int asked = 0;
            IntPtr first = AddressCache.Code(Target, Target, 1,
                () => { asked++; return new IntPtr(0x1234); });
            Console.WriteLine("  call 1 (no module): asked=" + asked +
                              " result=0x" + first.ToInt64().ToString("X"));

            // 2. The module arrives.
            if (LoadLibraryW(fake) == IntPtr.Zero)
            {
                Console.WriteLine("  the fake module did not load");
                return 2;
            }

            IntPtr module = GameAssembly.Base;
            Console.WriteLine("  loaded the fake module: base=0x" + module.ToInt64().ToString("X") +
                              " size=0x" + GameAssembly.SizeOfImage.ToString("X") +
                              " identity=" + GameAssembly.Identity);

            // 3. The same target is asked again, now that there is an image to record it in.
            int askedAgain = 0;
            IntPtr second = AddressCache.Code(Target, Target, 1,
                () => { askedAgain++; return new IntPtr(module.ToInt64() + TargetRva); });
            Console.WriteLine("  call 2 (module here): asked=" + askedAgain +
                              " result=0x" + second.ToInt64().ToString("X"));

            AddressCache.Flush();

            bool wantHit = mode == "hit";
            bool hit = askedAgain == 0;
            Console.WriteLine("  expected " + (wantHit ? "a cache hit" : "a live resolve") +
                              ": " + (hit == wantHit ? "yes" : "NO"));
            return hit == wantHit ? 0 : 1;
        }

        /// <summary>Runs one session in its own process, so the cache starts from nothing.</summary>
        private static int Run(string fake, string mode)
        {
            var start = new ProcessStartInfo
            {
                FileName = Environment.ProcessPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };

            // Started through "dotnet <dll>", the host has to be told which assembly to run.
            string host = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            if (string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Assembly.GetEntryAssembly().Location);

            start.ArgumentList.Add(mode);
            start.ArgumentList.Add(fake);

            using (Process child = Process.Start(start))
            {
                string output = child.StandardOutput.ReadToEnd();
                child.WaitForExit();
                Console.Write(output);
                return child.ExitCode;
            }
        }

        /// <summary>The cache file, as it stands.</summary>
        private static void Show(string cache)
        {
            Console.WriteLine("  cache file: " + cache);
            if (!File.Exists(cache))
            {
                Console.WriteLine("  (there is none)");
                return;
            }

            foreach (string line in File.ReadAllLines(cache)) Console.WriteLine("  | " + line);
        }

        /// <summary>The file to load as the fake module: the apphost, or the managed assembly.</summary>
        private static string FakeSource()
        {
            string process = Environment.ProcessPath;
            string host = process == null ? "" : Path.GetFileNameWithoutExtension(process);
            if (process != null && !string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase))
                return process;

            return Assembly.GetEntryAssembly().Location;
        }
    }
}
