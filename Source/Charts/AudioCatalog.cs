using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace InFalsusChartLoader_falsum
{
    /// <summary>The custom songs' audio: name to index, and index to bytes.</summary>
    internal static unsafe class AudioCatalog
    {
        private const string Extension = ".wav";

        /// <summary>One song's audio, and the index it answers to.</summary>
        private sealed class Entry
        {
            internal int Index;
            internal AudioCodec.Prepared Audio;
        }

        private static readonly Dictionary<string, Entry> ByBase =
            new Dictionary<string, Entry>(StringComparer.Ordinal);

        private static readonly Dictionary<int, Entry> ByIndex = new Dictionary<int, Entry>();

        /// <summary>Reads served, bytes handed over, and names resolved.</summary>
        internal static long Served, ServedBytes, Resolved;

        /// <summary>The index a song's audio answers to, or -1.</summary>
        internal static int IndexOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) ? entry.Index : -1;

        /// <summary>The byte length of a song's prepared audio, or -1.</summary>
        internal static int BytesOf(string baseName) =>
            ByBase.TryGetValue(baseName, out Entry entry) ? entry.Audio.Length : -1;

        /// <summary>Reads a song's audio without registering it with the game.</summary>
        /// <remarks>
        /// The reload needs the two halves apart: reading has no side effect and can be retried, while
        /// registering asks the game's asset manager for a slot that cannot be given back.
        /// </remarks>
        internal static bool Prepare(string path, out AudioCodec.Prepared prepared, out string reason)
        {
            reason = null;
            prepared = AudioCodec.Load(path, out reason);
            return prepared != null;
        }

        /// <summary>Gives a prepared song its index and makes it answer for that name.</summary>
        internal static bool Adopt(string baseName, AudioCodec.Prepared prepared, out string reason)
        {
            reason = null;

            if (prepared == null)
            {
#if DEBUG
                reason = "no prepared audio to register";
#endif
                return false;
            }

            int index = StreamingAssets.Add(prepared.Length);
            if (index < 0)
            {
                reason = "the game's asset manager would not take another file";
                return false;
            }

            var entry = new Entry { Index = index, Audio = prepared };
            ByBase[baseName] = entry;
            ByIndex[index] = entry;
            Diagnostics.Info($"audio '{baseName}' -> index {index}, {prepared.Length} bytes");
            return true;
        }

        /// <summary>Prepares a song's audio and assigns it an index.</summary>
        internal static bool Add(string baseName, string path, out string reason)
        {
            if (!Prepare(path, out AudioCodec.Prepared prepared, out reason)) return false;

            return Adopt(baseName, prepared, out reason);
        }

        /// <summary>Forgets every song's audio, so a reload registers the files on disk now.</summary>
        /// <remarks>The indices already handed to the game's asset manager cannot be reclaimed.</remarks>
        internal static void Reset()
        {
            ByBase.Clear();
            ByIndex.Clear();
        }

        /// <summary>Resolves a custom song's audio name to its index.</summary>
        internal static bool TryResolve(IntPtr namePtr, IntPtr indexPtr)
        {
            if (!CouldBeAudio(namePtr)) return false;

            string name = Memory.Text(namePtr, 128);
            if (name == null) return false;
            if (!name.EndsWith(Extension, StringComparison.Ordinal)) return false;

            string baseName = name.Substring(0, name.Length - Extension.Length);
            if (baseName.Length == 0) return false;
            if (!ByBase.TryGetValue(baseName, out Entry entry)) return false;

            Memory.WriteI32(indexPtr, entry.Index);
            Resolved++;
            return true;
        }

        /// <summary>Whether a name could be an audio file, without materialising it.</summary>
        private static unsafe bool CouldBeAudio(IntPtr s)
        {
            if (!Memory.LooksLikeObject(s)) return false;

            try
            {
                int length = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_length(s);
                if (length < Extension.Length + 1 || length > 128) return false;

                char* chars = Il2CppInterop.Runtime.IL2CPP.il2cpp_string_chars(s);
                if (chars == null) return false;

                return chars[length - 1] == 'v';
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Serves one read of a custom song's audio.</summary>
        internal static bool TryServe(IntPtr record)
        {
            if (Hooks.CompleteRead == null) return false;

            long handle = Memory.I64(record + Offsets.Runtime.ReadRecord.Handle);
            int index = (int)(handle & Offsets.Runtime.ReadRecord.IndexMask);
            if (!ByIndex.TryGetValue(index, out Entry entry)) return false;

            IntPtr info = Memory.Ptr(record + Offsets.Runtime.ReadRecord.Info);
            if (info == IntPtr.Zero) return false;

            int offset = Memory.I32(info + Offsets.Runtime.ReadRecord.Offset);
            int size = Memory.I32(info + Offsets.Runtime.ReadRecord.Length);
            IntPtr buffer = Memory.Ptr(info + Offsets.Runtime.ReadRecord.Buffer);
            if (buffer == IntPtr.Zero || size <= 0) return false;

            byte[] bytes = entry.Audio.Bytes;
            int available = offset >= 0 && offset < bytes.Length ? bytes.Length - offset : 0;
            int count = Math.Min(size, available);

            if (count > 0) Marshal.Copy(bytes, offset, buffer, count);

            Served++;
            ServedBytes += count;

            Memory.WriteI32(info + Offsets.Runtime.ReadRecord.BytesRead, count);

            PendingReads.Finished(record);

            byte* copy = stackalloc byte[Offsets.Runtime.ReadRecord.Size];
            Buffer.MemoryCopy((void*)record, copy, Offsets.Runtime.ReadRecord.Size,
                              Offsets.Runtime.ReadRecord.Size);
            Hooks.CompleteRead((IntPtr)copy, 0);
            return true;
        }
    }
}
