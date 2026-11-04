using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InFalsusChartLoader_falsum
{
    /// <summary>One resolved address, and everything needed to tell whether it is still the same one.</summary>
    /// <remarks>
    /// Everything here is plain managed code: no Unity, no IL2CPP, no <see cref="Diagnostics"/>, so the
    /// rule tests can link this file and cover it without a running game.
    /// </remarks>
    internal sealed class AddressEntry
    {
        /// <summary>The lookup key: the method's name and its arity, in one string.</summary>
        internal string Id;

        /// <summary>What the entry was resolved from, so another route cannot reuse it.</summary>
        internal string Identity;

        /// <summary>How many parameters the method takes.</summary>
        internal int Arity;

        /// <summary>The address, relative to the game module's base.</summary>
        internal long Rva;

        /// <summary>The first bytes of the method, so a stale address can be spotted.</summary>
        internal byte[] Prologue;
    }

    /// <summary>The cache's records: how they are keyed, written, read back and checked.</summary>
    /// <remarks>
    /// A record is one line of five tab-separated fields:
    /// <c>Id \t Identity \t Arity \t Rva \t PrologueHex</c>. Every record ends with a newline, the
    /// last one included, so the cache is an ordinary line-oriented text file. The format is written
    /// by hand on purpose: it has to live in a plain console test project, and a cache file has to
    /// stay readable when it is opened by hand.
    /// </remarks>
    internal static class AddressCacheEntries
    {
        /// <summary>How many bytes of the target the cache keeps, to recognise it later.</summary>
        internal const int PrologueLength = 16;

        /// <summary>How many fields one record holds.</summary>
        private const int FieldCount = 5;

        /// <summary>The key a cache belongs to: the game it was taken from, and the mod that wrote it.</summary>
        internal static string Key(string gameIdentity, string modVersion)
        {
            if (string.IsNullOrEmpty(gameIdentity) || string.IsNullOrEmpty(modVersion)) return string.Empty;
            return gameIdentity + "|" + modVersion;
        }

        /// <summary>The records as text: one line each, tab-separated, every line ended by a newline.</summary>
        /// <remarks>
        /// A record whose id, identity or prologue cannot be written the way a reader expects it is
        /// left out rather than written badly: one bad line would throw the whole cache away.
        /// </remarks>
        internal static string Serialize(List<AddressEntry> entries)
        {
            if (entries == null || entries.Count == 0) return string.Empty;

            var text = new StringBuilder();
            foreach (AddressEntry entry in entries)
            {
                if (!Writable(entry)) continue;

                text.Append(entry.Id).Append('\t')
                    .Append(entry.Identity).Append('\t')
                    .Append(entry.Arity.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(entry.Rva.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(Hex(entry.Prologue))
                    .Append('\n');
            }

            return text.ToString();
        }

        /// <summary>The records the text holds, or false when the text is not a cache.</summary>
        /// <remarks>
        /// Every line that holds something has to be a whole record. A file that was cut short, or
        /// written by something else, is refused as a whole: half a cache would hand out addresses
        /// that belong to nothing. A blank line is a line ending, not damage, so it is skipped.
        /// </remarks>
        internal static bool TryParse(string text, out List<AddressEntry> entries)
        {
            entries = null;
            if (string.IsNullOrEmpty(text)) return false;

            var parsed = new List<AddressEntry>();

            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;

                string[] fields = line.Split('\t');
                if (fields.Length != FieldCount) return false;
                if (!SafeField(fields[0]) || !SafeField(fields[1])) return false;
                if (!int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int arity))
                    return false;
                if (!long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out long rva))
                    return false;

                byte[] prologue = Unhex(fields[4]);
                if (prologue == null) return false;

                parsed.Add(new AddressEntry
                {
                    Id = fields[0],
                    Identity = fields[1],
                    Arity = arity,
                    Rva = rva,
                    Prologue = prologue,
                });
            }

            if (parsed.Count == 0) return false;

            entries = parsed;
            return true;
        }

        /// <summary>Whether an address lies inside the game module's image.</summary>
        internal static bool InsideImage(long rva, long imageSize)
        {
            return rva >= 0 && imageSize > 0 && rva < imageSize;
        }

        /// <summary>Whether the bytes at the target are still the ones the cache remembers.</summary>
        internal static bool PrologueMatches(byte[] cached, byte[] actual)
        {
            if (cached == null || actual == null) return false;
            if (cached.Length != PrologueLength || actual.Length != PrologueLength) return false;

            for (int i = 0; i < PrologueLength; i++)
                if (cached[i] != actual[i]) return false;

            return true;
        }

        /// <summary>Whether a text field can go on a line: not empty, and free of the line's separators.</summary>
        private static bool SafeField(string field)
        {
            if (string.IsNullOrEmpty(field)) return false;
            return field.IndexOf('\t') < 0 && field.IndexOf('\n') < 0 && field.IndexOf('\r') < 0;
        }

        /// <summary>Whether a record can be written the way a reader expects to read it back.</summary>
        /// <remarks>
        /// The reader takes the arity and the rva as plain unsigned decimals, so a record that would
        /// need a minus sign is left out instead of being written in a shape that would throw the
        /// whole file away.
        /// </remarks>
        private static bool Writable(AddressEntry entry)
        {
            if (entry == null) return false;
            if (!SafeField(entry.Id) || !SafeField(entry.Identity)) return false;
            if (entry.Arity < 0 || entry.Rva < 0) return false;
            return entry.Prologue != null && entry.Prologue.Length == PrologueLength;
        }

        /// <summary>Bytes as lowercase hex, two characters each.</summary>
        private static string Hex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                text.Append(HexDigit(b >> 4));
                text.Append(HexDigit(b & 0xF));
            }
            return text.ToString();
        }

        /// <summary>One hex digit, in lowercase.</summary>
        private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

        /// <summary>The bytes 32 hex characters stand for, or null when they are not.</summary>
        private static byte[] Unhex(string text)
        {
            if (text == null || text.Length != PrologueLength * 2) return null;

            var bytes = new byte[PrologueLength];
            for (int i = 0; i < PrologueLength; i++)
            {
                int high = Digit(text[i * 2]);
                int low = Digit(text[i * 2 + 1]);
                if (high < 0 || low < 0) return null;
                bytes[i] = (byte)((high << 4) | low);
            }
            return bytes;
        }

        /// <summary>The value of one hex character, or -1 when it is not one.</summary>
        private static int Digit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
