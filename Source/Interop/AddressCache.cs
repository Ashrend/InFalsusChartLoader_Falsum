using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The resolved addresses of the game's methods, kept between runs.</summary>
    /// <remarks>
    /// The addresses live in a text file under the user's local application data: its first line
    /// is the key, and every line after it is one record. A record is only used when the target
    /// still looks the way it did when the record was written - the same identity, an address
    /// inside the game module, and the same first bytes - so a stale file costs time, never
    /// correctness.
    /// </remarks>
    internal static class AddressCache
    {
        /// <summary>The folder the cache file lives in, under the user's local application data.</summary>
        private const string FolderName = "InFalsusChartLoader_falsum";

        /// <summary>The cache file's name; the format's version is part of it.</summary>
        private const string FileName = "addresses.v1";

        /// <summary>What the first line of the file starts with.</summary>
        private const string KeyMarker = "KEY\t";

        /// <summary>The records read from the file, and the ones written this session, by id.</summary>
        private static readonly Dictionary<string, AddressEntry> Records = new Dictionary<string, AddressEntry>();

        /// <summary>The targets that are never cached: their address is not the same every run.</summary>
        private static readonly HashSet<string> NotCacheable = new HashSet<string>();

        /// <summary>The targets this session could not resolve; asking again costs seconds.</summary>
        private static readonly HashSet<string> Unresolved = new HashSet<string>();

        /// <summary>Whether the file has been looked at already.</summary>
        private static bool _loaded;

        /// <summary>Whether there is a game module to key the cache on.</summary>
        private static bool _enabled;

        /// <summary>Whether the records changed since the file was written.</summary>
        private static bool _changed;

#if DEBUG
        /// <summary>Whether the file was read and its records taken: the difference between loaded and absent.</summary>
        /// <remarks>Debug only: it exists to word the summary, and the summary is Debug only.</remarks>
        private static bool _read;
#endif

        /// <summary>The key the records belong to.</summary>
        private static string _key;

        /// <summary>How many lookups a record answered: the cheap path.</summary>
        internal static int Restored;

        /// <summary>How many lookups had to call the resolver: a miss, for any reason.</summary>
        internal static int Resolved;

        /// <summary>How many records were looked at and refused: stale, out of range, or the wrong shape.</summary>
        internal static int Rejected;

        /// <summary>The file the cache is kept in.</summary>
        internal static string Path
        {
            get
            {
                string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return System.IO.Path.Combine(local, FolderName, FileName);
            }
        }

        /// <summary>A target's code pointer: from the cache when it is still good, else by resolving it.</summary>
        /// <param name="id">The target's name in the cache; it must tell one target from another.</param>
        /// <param name="identity">
        /// The lookup key repeated: for the name-based routes everything the caller knows before
        /// the lookup - the route, type, method and image - is already in the id, so the record
        /// keeps the same string rather than a second one to compare it with.
        /// </param>
        /// <param name="arity">
        /// How many parameters the method takes, or zero when the caller cannot tell before the
        /// lookup; a known count is checked against the record before it is restored.
        /// </param>
        /// <param name="resolve">The slow way to the address, used when the cache cannot answer.</param>
        internal static IntPtr Code(string id, string identity, int arity, Func<IntPtr> resolve)
        {
            Load();

            // A target that is never cached is asked for every time: there is no point in
            // looking it up first.
            if (NotCacheable.Contains(id))
            {
                Resolved++;
                return resolve();
            }

            if (_enabled && Records.TryGetValue(id, out AddressEntry record))
            {
                IntPtr restored = Restore(record, identity, arity);
                if (restored != IntPtr.Zero) return restored;
            }

            // A target that could not be resolved is not asked again: one attempt already costs
            // more than a second, and the answer would not be any different.
            if (Unresolved.Contains(id)) return IntPtr.Zero;

            Resolved++;
            IntPtr resolved = resolve();
            if (resolved == IntPtr.Zero)
            {
                Unresolved.Add(id);
                return IntPtr.Zero;
            }

            Remember(id, identity, arity, resolved);
            return resolved;
        }

        /// <summary>Rules a target out of the cache: its address is not the same every run.</summary>
        /// <param name="id">The target's name in the cache.</param>
        internal static void Forget(string id)
        {
            NotCacheable.Add(id);
        }

        /// <summary>Writes the records to the cache file, if anything changed.</summary>
        /// <remarks>
        /// The file is written beside itself and moved into place, so a run that dies halfway
        /// leaves the old file alone instead of half a new one. A cache that cannot be written
        /// is not a failure of the mod: the addresses are resolved again next time.
        /// </remarks>
        internal static void Flush()
        {
            if (!_changed || !_enabled) return;

            string path = Path;
            string folder = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(folder) || !System.IO.Path.IsPathRooted(folder))
            {
                Diagnostics.Info("the address cache has no absolute folder; it is not written");
                return;
            }

            string text = KeyMarker + _key + "\n" +
                          AddressCacheEntries.Serialize(new List<AddressEntry>(Records.Values));
            string temp = path + ".tmp";

            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(temp, text);
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception e)
            {
                Diagnostics.Info($"the address cache could not be written: {Diagnostics.Describe(e)}");
                return;
            }

            _changed = false;
        }

        /// <summary>What the cache did this start: one line, in the DEBUG channel.</summary>
        /// <remarks>
        /// Called after each tier has asked for every address it needs, so the line printed at the
        /// end of the deferred install covers the whole session while the one printed at startup
        /// does not. A cache that was never read says so: an inert cache must not be printed as a
        /// working one that restored nothing, so the status word, not the counters, carries that.
        /// </remarks>
        [System.Diagnostics.Conditional("DEBUG")]
        internal static void Report()
        {
#if DEBUG
            string key = string.IsNullOrEmpty(_key) ? "-" : _key;
            Diagnostics.Info($"address cache: {(_read ? "loaded" : "absent")} key={key} " +
                             $"records={Records.Count} restored={Restored} resolved={Resolved} " +
                             $"rejected={Rejected}");
#endif
        }

        /// <summary>Reads the file once, and remembers whether the cache is of any use.</summary>
        /// <remarks>
        /// Without the game module there is nothing to key the cache on, so the cache stays
        /// inert: nothing is read, nothing is written, and every target is resolved live.
        /// The module may still be on its way, so "not there yet" is not remembered as "read
        /// already": the identity is asked again the next time.
        /// </remarks>
        private static void Load()
        {
            if (_loaded) return;

            string identity = GameAssembly.Identity;
            if (identity == null)
            {
                // The game module is not there. The flag is deliberately not set: the module
                // may still load, and the identity is askable again. Latching it here would
                // keep the whole session's cache inert after one early call, and that call
                // is not made twice - the startup would stay slow, and say nothing about it.
                Diagnostics.Info("the game module is not there; the address cache stays inert");
                return;
            }

            _loaded = true;
            _enabled = true;
            _key = AddressCacheEntries.Key(identity, ModCore.Version);

            string text;
            try
            {
                text = File.ReadAllText(Path);
            }
            catch (FileNotFoundException)
            {
                return;
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
            catch (Exception e)
            {
                Diagnostics.Info($"the address cache could not be read: {Diagnostics.Describe(e)}");
                return;
            }

            // The first line is the key, and the records are the lines after it. The key line
            // holds two fields, not the five a record holds, so it must be taken off before the
            // records are read: handed over as it is, it would look like damage and would
            // throw the whole file away.
            int newline = text.IndexOf('\n');
            string first = (newline < 0 ? text : text.Substring(0, newline)).TrimEnd('\r');
            string body = newline < 0 ? string.Empty : text.Substring(newline + 1);

            if (!first.StartsWith(KeyMarker, StringComparison.Ordinal))
            {
                Diagnostics.Info("the address cache has no key line; it is ignored");
                return;
            }

            if (first.Substring(KeyMarker.Length) != _key)
            {
                Diagnostics.Info("the address cache belongs to another build; it is ignored");
                return;
            }

            if (!AddressCacheEntries.TryParse(body, out List<AddressEntry> parsed))
            {
                Diagnostics.Info("the address cache holds no usable records; it is ignored");
                return;
            }

            foreach (AddressEntry record in parsed) Records[record.Id] = record;

#if DEBUG
            _read = true;
#endif
        }

        /// <summary>The address a record points at, when it still points at the same code.</summary>
        /// <param name="record">The record read from the file.</param>
        /// <param name="identity">What the caller asked for; the record has to be for the same thing.</param>
        /// <param name="arity">The parameter count the caller knows before the lookup, or zero.</param>
        /// <returns>The address, or zero when the record cannot be trusted any more.</returns>
        /// <remarks>Every refusal is counted: the summary says how often the cache was looked at and turned down.</remarks>
        private static IntPtr Restore(AddressEntry record, string identity, int arity)
        {
            if (record.Identity != identity)
            {
                Rejected++;
                return IntPtr.Zero;
            }

            if (!AddressCacheEntries.InsideImage(record.Rva, GameAssembly.SizeOfImage))
            {
                Rejected++;
                return IntPtr.Zero;
            }

            // The arity is a cheap consistency check, not a selector: it is what the caller could
            // work out before asking, which is nothing at all for the runtime route. A record whose
            // arity disagrees was made for a different shape of the same name.
            if (arity > 0 && record.Arity != arity)
            {
                Rejected++;
                return IntPtr.Zero;
            }

            IntPtr target = new IntPtr(GameAssembly.Base.ToInt64() + record.Rva);
            byte[] actual = Prologue(target);
            if (!AddressCacheEntries.PrologueMatches(record.Prologue, actual))
            {
                Rejected++;
                Diagnostics.Info($"the cached address of {record.Id} does not match its bytes; " +
                                 "the target is resolved again");
                return IntPtr.Zero;
            }

            Restored++;
            return target;
        }

        /// <summary>Puts a resolved address into the cache, when it is one that may be cached.</summary>
        /// <param name="id">The target's name in the cache.</param>
        /// <param name="identity">What the target was resolved from.</param>
        /// <param name="arity">How many parameters the method takes.</param>
        /// <param name="resolved">The address that was resolved.</param>
        private static void Remember(string id, string identity, int arity, IntPtr resolved)
        {
            long size = GameAssembly.SizeOfImage;
            if (size <= 0)
            {
                // The size of the image is unknown: the game module is not loaded, or its header
                // cannot be read. Nothing is written down and nothing is ruled out either - the
                // target is asked again the next time. Ruling it out here would keep it out of
                // the cache for good after one early call, and that call is not made twice.
                Diagnostics.Info($"the size of the game module is unknown; {id} is not cached now");
                return;
            }

            long rva = resolved.ToInt64() - GameAssembly.Base.ToInt64();
            if (!AddressCacheEntries.InsideImage(rva, size))
            {
                // The address is outside the module: the code was made while the game was
                // running, so its address is a different one every time. It is never cached.
                NotCacheable.Add(id);
                Diagnostics.Info($"{id} is outside the game module; it is never cached");
                return;
            }

            byte[] prologue = Prologue(resolved);
            if (prologue == null) return;

            Records[id] = new AddressEntry
            {
                Id = id,
                Identity = identity,
                Arity = arity,
                Rva = rva,
                Prologue = prologue,
            };
            _changed = true;
        }

        /// <summary>The first bytes of a target, or null when they cannot be read.</summary>
        /// <remarks>
        /// The fingerprint is <see cref="AddressCacheEntries.PrologueLength"/> bytes long, and
        /// it is read here rather than through the hooks' own helper: that one reads eight
        /// bytes by default, and a fingerprint of the wrong length never matches, which would
        /// leave the cache empty without a word about it.
        /// </remarks>
        /// <param name="target">The address to read.</param>
        private static byte[] Prologue(IntPtr target)
        {
            if (target == IntPtr.Zero) return null;

            var bytes = new byte[AddressCacheEntries.PrologueLength];
            try
            {
                Marshal.Copy(target, bytes, 0, bytes.Length);
            }
            catch (Exception)
            {
                // The target cannot be read: there is no fingerprint to compare.
                return null;
            }

            return bytes;
        }
    }
}
