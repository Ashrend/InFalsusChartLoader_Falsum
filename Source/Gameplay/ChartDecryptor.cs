using System;
using System.Buffers.Binary;

namespace InFalsusChartLoader_falsum
{
    internal static class ChartDecryptor
    {
        private const int HeaderSize = 28;
        private const int NoteSize = 80;

        internal static void DeriveKey(string seed, int count, ushort u16, out ulong s0, out ulong s1, out ulong s2, out ulong s3, out uint counter)
        {
            s0 = 0xC4CEB9FE1A85EC53UL + (uint)count;
            s1 = ((ulong)(uint)count << 32) ^ 0x9E3779B185EBCA87UL;
            s2 = 0xD1B54A32D192ED03UL + u16;
            s3 = (ulong)u16 ^ 0x94D049BB133111EBUL;
            counter = 0;

            for (int i = 0; i < seed.Length; i++)
            {
                ulong rdx = ((ulong)(uint)i << 32) | (uint)seed[i];
                ulong rdi = (ulong)u16 ^ rdx;
                ulong r8 = rdx + (uint)count;
                ulong v = counter & 3;
                if (v == 0)
                {
                    s0 += RotateLeft(s3, 7) + rdx;
                    ulong rax = RotateLeft(s0 + r8, 13) ^ s1;
                    s1 = rax;
                    rdi ^= rax;
                    s2 -= rdi;
                    s3 = RotateLeft(s3 + s2, 27) ^ s0;
                }
                else if (v == 1)
                {
                    s1 = RotateLeft(s0, 9) + s1 + rdi;
                    ulong rcx = RotateLeft(s1 + rdx, 19) ^ s2;
                    s2 = rcx;
                    r8 ^= rcx;
                    s3 -= r8;
                    s0 = RotateLeft(s0 + s3, 31) ^ s1;
                }
                else if (v == 2)
                {
                    s2 = RotateLeft(s1, 15) + r8 + s2;
                    ulong rcx = RotateLeft(s2 + rdi, 23) ^ s3;
                    rdx ^= rcx;
                    s3 = rcx;
                    s0 -= rdx;
                    s1 = RotateLeft(s1 + s0, 39) ^ s2;
                }
                else
                {
                    ulong keep = s2;
                    s3 = RotateLeft(s2, 21) + s3 + rdx;
                    ulong rcx = RotateLeft(s3 + r8, 29);
                    s0 ^= rcx;
                    rdi ^= s0;
                    s1 -= rdi;
                    s2 = RotateLeft(keep + s1, 43) ^ s3;
                }
                counter++;
            }

            Finalize(ref s0, ref s1, ref s2, ref s3, ref counter, (uint)count, u16, (ulong)seed.Length);
        }

        internal static bool Overwrite(IntPtr items, int count, int stride, byte[] bytes, string seed)
        {
            if (stride < 0x80 || bytes == null || bytes.Length < HeaderSize) return false;
            if (bytes[0] != (byte)'I' || bytes[1] != (byte)'C' || bytes[2] != (byte)'P' || bytes[3] != (byte)'1')
                return false;

            int header = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6));
            int record = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8));
            int notes = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(12));
            if (record < NoteSize || header + notes * record > bytes.Length) return false;

            var decoded = new Note[notes];
            DeriveKey(seed, notes, 1, out ulong s0, out ulong s1, out ulong s2, out ulong s3, out uint counter);

            for (int i = 0; i < notes; i++)
            {
                int at = header + i * record;
                Span<byte> note = bytes.AsSpan(at, NoteSize);
                ulong p0 = BinaryPrimitives.ReadUInt64LittleEndian(note) - Gen(s0, s1, s2, s3, i, 0);
                ulong p1 = BinaryPrimitives.ReadUInt64LittleEndian(note.Slice(8)) - Gen(s0, s1, s2, s3, i, 1);
                ulong p2 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x10)) - (uint)Gen(s0, s1, s2, s3, i, 2));
                ulong p3 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x14)) - (uint)Gen(s0, s1, s2, s3, i, 3));
                ulong p4 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x18)) - (uint)Gen(s0, s1, s2, s3, i, 4));
                ulong p5 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x1C)) - (uint)Gen(s0, s1, s2, s3, i, 5));
                ulong p6 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x20)) - (uint)Gen(s0, s1, s2, s3, i, 6));
                ulong p7 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x24)) - (uint)Gen(s0, s1, s2, s3, i, 7));
                ulong p8 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x28)) - (uint)Gen(s0, s1, s2, s3, i, 8));
                ulong p9 = (uint)(BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x2C)) - (uint)Gen(s0, s1, s2, s3, i, 9));

                uint[] plain = new uint[8];
                for (int j = 0; j < 8; j++)
                    plain[j] = BinaryPrimitives.ReadUInt32LittleEndian(note.Slice(0x30 + j * 4));

                var scales = new uint[8];
                for (int j = 0; j < 4; j++)
                {
                    uint a = plain[j * 2];
                    uint b = plain[j * 2 + 1];
                    uint g = Gcd(a, b);
                    if (g == 0) g = 1;
                    ulong field = j == 0 ? p6 : j == 1 ? p7 : j == 2 ? p8 : p9;
                    scales[j * 2] = (uint)((ulong)(a / g) * field);
                    scales[j * 2 + 1] = (uint)((ulong)(b / g) * field);
                }

                int laneFirst = 1;
                int laneLast = 1;
                if (scales[1] != 0 && scales[5] != 0)
                {
                    double x = (double)(int)scales[0] / (int)scales[1];
                    double width = (double)(int)scales[4] / (int)scales[5];
                    laneFirst = (int)Math.Round(x * 4.0);
                    laneLast = (int)Math.Round((x + width) * 4.0) - 1;
                    laneFirst = laneFirst < 0 ? 0 : laneFirst > 5 ? 5 : laneFirst;
                    laneLast = laneLast < 0 ? 0 : laneLast > 5 ? 5 : laneLast;
                    if (laneLast < laneFirst) laneLast = laneFirst;
                }

                if ((int)(p4 & 0xFF) == 4)
                {
                    laneFirst = -1;
                    laneLast = -1;
                }

                decoded[i] = new Note
                {
                    Id = (long)p0,
                    Line = (long)p1,
                    Side = (int)(p4 & 0xFF),
                    Type = (int)(p4 >> 8),
                    Start = (int)(uint)p2,
                    End = (int)(uint)p3,
                    Flags = (int)(uint)p5,
                    N0 = (int)scales[0],
                    D0 = (int)scales[1],
                    N1 = (int)scales[2],
                    D1 = (int)scales[3],
                    N2 = (int)scales[4],
                    D2 = (int)scales[5],
                    N3 = (int)scales[6],
                    D3 = (int)scales[7],
                    LaneFirst = laneFirst,
                    LaneLast = laneLast,
                };

                Finalize(ref s0, ref s1, ref s2, ref s3, ref counter, p0, p1, (p3 << 32) | (uint)p2);
                Finalize(ref s0, ref s1, ref s2, ref s3, ref counter, p4, p5, ((ulong)scales[1] << 32) | scales[0]);
                Finalize(ref s0, ref s1, ref s2, ref s3, ref counter,
                    ((ulong)scales[3] << 32) | scales[2],
                    ((ulong)scales[5] << 32) | scales[4],
                    ((ulong)scales[7] << 32) | scales[6]);
            }

            int write = Math.Min(count, notes);
            for (int i = 0; i < write; i++)
            {
                IntPtr p = items + i * stride;
                Note n = decoded[i];
#if DEBUG
                if (i < 4 || (n.Type == 5 && i < 40))
                    Diagnostics.Info($"local note[{i}] pre lane {Memory.I32(p + 0x78)}..{Memory.I32(p + 0x7C)}; " +
                                     $"pre extras Ee={Memory.I64(p + 0x50):X16} fe={Memory.I64(p + 0x58):X16} " +
                                     $"Fe={Memory.I64(p + 0x60):X16} ge={Memory.I64(p + 0x68):X16}; " +
                                     $"decoded lane {n.LaneFirst}..{n.LaneLast}, side {n.Side}, type {n.Type}, " +
                                     $"time {n.Start}..{n.End}");
#endif
                Memory.WriteI64(p, n.Id);
                Memory.WriteI64(p + 8, n.Line);
                Memory.WriteI32(p + 0x10, n.Side);
                Memory.WriteI32(p + 0x14, n.Type);
                Memory.WriteI32(p + 0x18, n.Start);
                Memory.WriteI32(p + 0x1C, n.End);
                WriteFraction(p + 0x20, n.N0, n.D0);
                WriteFraction(p + 0x2C, n.N1, n.D1);
                WriteFraction(p + 0x38, n.N2, n.D2);
                WriteFraction(p + 0x44, n.N3, n.D3);
                long freeze = BitConverter.DoubleToInt64Bits(-1800000.0);
                Memory.WriteI64(p + 0x50, 0);
                Memory.WriteI64(p + 0x58, freeze);
                Memory.WriteI64(p + 0x60, freeze);
                Memory.WriteI64(p + 0x68, 0);
                if (n.Side == 4)
                {
                    JitterSeeds(n, out ulong eeSeed, out ulong geSeed);
                    Memory.WriteI64(p + 0x50, BitConverter.DoubleToInt64Bits((double)eeSeed));
                    Memory.WriteI64(p + 0x68, (long)geSeed);
                }
                Memory.WriteI32(p + 0x70, 0);
                Memory.WriteI32(p + 0x74, n.Flags);
                Memory.WriteI32(p + 0x78, n.LaneFirst);
                Memory.WriteI32(p + 0x7C, n.LaneLast);
            }

            if (write != count)
                Diagnostics.Warn($"local decoder produced {notes} notes; the game list holds {count}");
            return write != 0;
        }

        private static void AssignLanes(Note[] notes)
        {
            var byStart = new Dictionary<int, List<int>>();
            for (int i = 0; i < notes.Length; i++)
            {
                int start = notes[i].Start;
                if (!byStart.TryGetValue(start, out List<int> list))
                {
                    list = new List<int>();
                    byStart[start] = list;
                }
                list.Add(i);
            }

            foreach (List<int> list in byStart.Values)
            {
                var used = new HashSet<int>();
                for (int n = 0; n < list.Count; n++)
                {
                    Note note = notes[list[n]];
                    if (note.Side >= 1 && note.Side <= 3 && note.LaneFirst == note.LaneLast)
                        used.Add(note.LaneFirst);
                }

                for (int n = 0; n < list.Count; n++)
                {
                    int at = list[n];
                    Note note = notes[at];
                    if (note.Side < 1 || note.Side > 3 || note.LaneLast <= note.LaneFirst) continue;

                    int first = note.LaneFirst;
                    int last = note.LaneLast;
                    int center = (first + last + 1) / 2;
                    int chosen = -1;
                    if (!used.Contains(center)) chosen = center;
                    if (chosen < 0)
                        for (int lane = first; lane <= last; lane++)
                            if (!used.Contains(lane)) { chosen = lane; break; }
                    if (chosen < 0) chosen = first;
                    note.LaneFirst = chosen;
                    notes[at] = note;
                    used.Add(chosen);
                }
            }
        }

        private static ulong Mix30(ulong value)
        {
            value = ((value >> 30) ^ value) * 0xBF58476D1CE4E5B9UL;
            return ((value >> 27) ^ value) * 0x94D049BB133111EBUL;
        }

        private static void JitterSeeds(Note note, out ulong ee, out ulong ge)
        {
            ulong id = (ulong)note.Id;
            ulong end = (uint)note.End;
            ulong start = (uint)note.Start;
            ulong type = (byte)note.Type;
            ulong a = (end << 39) | (end >> 25);
            a ^= start << 17;
            a ^= type << 56;
            a ^= id * 0x9E3779B185EBCA87UL;
            a ^= 0xD6E8FEB86659FD93UL;
            a = Mix30(a);
            ulong b = (end << 39) | (end >> 25);
            b ^= start << 17;
            b ^= type << 56;
            b ^= id * 0x9E3779B185EBCA87UL;
            b ^= 0xD6E8FEB86659FD93UL;
            b = Mix30(b);
            ge = (((a >> 31) ^ a) & 0xFFFFFFFFFFFFFUL) + 1;
            ee = (((b >> 31) ^ b) & 0xFFFFFFFFFFFFFUL) + 1;
        }

        private static void WriteFraction(IntPtr p, int num, int den)
        {
            Memory.WriteI32(p, num);
            Memory.WriteI32(p + 4, den);
            Memory.WriteF32(p + 8, den == 0 ? 0f : (float)num / den);
        }

        internal static ulong Gen(ulong s0, ulong s1, ulong s2, ulong s3, int idx, int k)
        {
            ulong r9 = ((ulong)(uint)idx << 32) | (uint)k;
            switch (k & 3)
            {
                case 3: return RotateLeft(s2 + s1 + r9, 37) ^ s3;
                case 2: r9 ^= s0; return RotateLeft(r9, 29) + s2 ^ s1;
                case 1: return (s1 - RotateLeft(s3 + r9, 17)) ^ s0;
                default: r9 ^= s2; return RotateLeft(r9, 11) + s0 ^ s3;
            }
        }

        internal static void Finalize(ref ulong s0, ref ulong s1, ref ulong s2, ref ulong s3, ref uint counter, ulong rdx, ulong r8, ulong r9)
        {
            switch (counter & 3)
            {
                case 0:
                    s0 += RotateLeft(s3, 7) + rdx;
                    ulong rax0 = RotateLeft(s0 + r8, 13);
                    s1 ^= rax0;
                    r9 ^= s1;
                    s2 -= r9;
                    s3 = RotateLeft(s2 + s3, 27) ^ s0;
                    break;
                case 1:
                    s1 += RotateLeft(s0, 9) + r9;
                    ulong rax1 = RotateLeft(s1 + rdx, 19);
                    s2 ^= rax1;
                    r8 ^= s2;
                    s3 -= r8;
                    s0 = RotateLeft(s3 + s0, 31) ^ s1;
                    break;
                case 2:
                    s2 += RotateLeft(s1, 15) + r8;
                    ulong rax2 = RotateLeft(s2 + r9, 23);
                    s3 ^= rax2;
                    rdx ^= s3;
                    s0 -= rdx;
                    s1 = RotateLeft(s1 + s0, 39) ^ s2;
                    break;
                default:
                    ulong keep = s2;
                    s3 += RotateLeft(s2, 21) + rdx;
                    ulong rax3 = RotateLeft(s3 + r8, 29);
                    s0 ^= rax3;
                    ulong rax4 = s0 ^ r9;
                    s1 -= rax4;
                    s2 = RotateLeft(keep + s1, 43) ^ s3;
                    break;
            }
            counter++;
        }

        internal static ulong RotateLeft(ulong value, int count) => (value << count) | (value >> (64 - count));

        internal static uint Gcd(uint a, uint b)
        {
            while (b != 0)
            {
                uint t = a % b;
                a = b;
                b = t;
            }
            return a;
        }

        private struct Note
        {
            internal long Id;
            internal long Line;
            internal int Side;
            internal int Type;
            internal int Start;
            internal int End;
            internal int Flags;
            internal int N0, D0, N1, D1, N2, D2, N3, D3;
            internal int LaneFirst, LaneLast;
        }
    }
}
