using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;

namespace InFalsusChartLoader_falsum
{
    internal static class FalsumChartCompiler
    {
        internal static byte[] Compile(string text, string seed)
        {
            Chart chart = Parse(text);
            return Encode(chart, seed);
        }

        private static Chart Parse(string text)
        {
            var chart = new Chart();
            var raw = new List<RawNote>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
            {
                string line = lines[lineNumber].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                int open = line.IndexOf('(');
                int close = line.LastIndexOf(')');
                if (open <= 0 || close <= open)
                    throw new FormatException($"chart line {lineNumber + 1}: invalid call");

                string name = line.Substring(0, open).Trim();
                string body = line.Substring(open + 1, close - open - 1);
                string[] parts = body.Split(',');
                for (int i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim();

                switch (name)
                {
                    case "chart":
                        chart.Bpm = ParseDouble(parts, 0, lineNumber);
                        chart.Division = ParseDouble(parts, 1, lineNumber);
                        break;
                    case "lane":
                        chart.Events.Add(new ChartEvent
                        {
                            Time = ParseInt(parts, 0, lineNumber),
                            Track = ParseInt(parts, 1, lineNumber),
                            Enabled = ParseInt(parts, 2, lineNumber),
                            Type = 3,
                            Id = chart.Events.Count,
                        });
                        break;
                    case "bpm":
                        chart.Events.Add(new ChartEvent
                        {
                            Time = ParseInt(parts, 0, lineNumber),
                            Type = 1,
                            Id = chart.Events.Count,
                            Value = ParseDouble(parts, 1, lineNumber),
                            Meter = parts.Length > 2 ? ParseDouble(parts, 2, lineNumber) : 0.0,
                        });
                        break;
                    case "track":
                        chart.Events.Add(new ChartEvent
                        {
                            Time = ParseInt(parts, 0, lineNumber),
                            Type = 0,
                            Id = chart.Events.Count,
                            Value = ParseDouble(parts, 1, lineNumber),
                        });
                        break;
                    case "visual":
                        chart.Events.Add(new ChartEvent
                        {
                            Time = ParseInt(parts, 0, lineNumber),
                            Type = 0,
                            Id = chart.Events.Count,
                            Value = ParseDouble(parts, 1, lineNumber),
                        });
                        break;
                    case "tap":
                    case "hold":
                    case "flick":
                    case "skyarea":
                    {
                        var values = new int[parts.Length];
                        for (int i = 0; i < parts.Length; i++)
                            values[i] = ParseInt(parts, i, lineNumber);
                        raw.Add(new RawNote { Kind = name, Values = values });
                        break;
                    }
                    default:
                        throw new FormatException($"chart line {lineNumber + 1}: unknown command {name}");
                }
            }

            var reserved = new HashSet<int>();
            foreach (RawNote note in raw)
                if (note.Kind == "skyarea" && note.Values.Length > 10)
                    reserved.Add(note.Values[10]);

            int cursor = 0;
            for (int i = 0; i < raw.Count; i++)
            {
                RawNote note = raw[i];
                int noteLine;
                if (note.Kind == "skyarea")
                {
                    if (note.Values.Length < 11)
                        throw new FormatException("skyarea needs a line value");
                    noteLine = note.Values[10];
                }
                else
                {
                    while (reserved.Contains(cursor)) cursor++;
                    noteLine = cursor++;
                }

                chart.Notes.Add(new ChartNote
                {
                    Kind = note.Kind,
                    Values = note.Values,
                    Index = i,
                    Line = noteLine,
                });
            }

            var lineGroups = new Dictionary<int, List<ChartNote>>();
            foreach (ChartNote note in chart.Notes)
            {
                if (!lineGroups.TryGetValue(note.Line, out List<ChartNote> group))
                {
                    group = new List<ChartNote>();
                    lineGroups[note.Line] = group;
                }
                group.Add(note);
            }
            var orderedGroups = new List<List<ChartNote>>(lineGroups.Values);
            orderedGroups.Sort((a, b) =>
            {
                int byStart = a[0].Values[0].CompareTo(b[0].Values[0]);
                if (byStart != 0) return byStart;
                int byFirst = a[0].Index.CompareTo(b[0].Index);
                return byFirst != 0 ? byFirst : a.Count.CompareTo(b.Count);
            });
            chart.Notes.Clear();
            for (int lineIndex = 0; lineIndex < orderedGroups.Count; lineIndex++)
            {
                List<ChartNote> group = orderedGroups[lineIndex];
                group.Sort((a, b) =>
                {
                    int byStart = a.Values[0].CompareTo(b.Values[0]);
                    return byStart != 0 ? byStart : a.Index.CompareTo(b.Index);
                });
                foreach (ChartNote note in group)
                {
                    note.Line = lineIndex;
                    note.Index = chart.Notes.Count;
                    chart.Notes.Add(note);
                }
            }

            return chart;
        }

        private static byte[] Encode(Chart chart, string seed)
        {
            byte[] data = new byte[28 + chart.Notes.Count * 80 + chart.Events.Count * 32];
            Span<byte> header = data.AsSpan(0, 28);
            header[0] = (byte)'I';
            header[1] = (byte)'C';
            header[2] = (byte)'P';
            header[3] = (byte)'1';
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(4), 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(6), 28);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(8), 80);
            BinaryPrimitives.WriteUInt16LittleEndian(header.Slice(10), 32);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(12), chart.Notes.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(16), chart.Events.Count);
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(20), BitConverter.SingleToInt32Bits((float)chart.Bpm));
            BinaryPrimitives.WriteInt32LittleEndian(header.Slice(24), BitConverter.SingleToInt32Bits((float)chart.Division));

            ChartDecryptor.DeriveKey(seed, chart.Notes.Count, 1,
                out ulong s0, out ulong s1, out ulong s2, out ulong s3, out uint counter);

            int at = 28;
            for (int index = 0; index < chart.Notes.Count; index++)
            {
                NoteSemantics sem = Semantics(chart.Notes[index]);
                Span<byte> record = data.AsSpan(at, 80);
                for (int k = 0; k < 10; k++)
                {
                    int offset = k == 0 ? 0 : k == 1 ? 8 : 0x10 + (k - 2) * 4;
                    int size = k < 2 ? 8 : 4;
                    ulong generated = ChartDecryptor.Gen(s0, s1, s2, s3, index, k);
                    if (size == 8)
                        BinaryPrimitives.WriteUInt64LittleEndian(record.Slice(offset), sem.Fields[k] + generated);
                    else
                        BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(offset), (uint)sem.Fields[k] + (uint)generated);
                }

                for (int i = 0; i < 8; i++)
                    BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(0x30 + i * 4), sem.Plain[i]);

                ChartDecryptor.Finalize(ref s0, ref s1, ref s2, ref s3, ref counter,
                    sem.Fields[0], sem.Fields[1], (sem.Fields[3] << 32) | sem.Fields[2]);
                ChartDecryptor.Finalize(ref s0, ref s1, ref s2, ref s3, ref counter,
                    sem.Fields[4], sem.Fields[5], ((ulong)(uint)sem.Pairs[0, 1] << 32) | (uint)sem.Pairs[0, 0]);
                ChartDecryptor.Finalize(ref s0, ref s1, ref s2, ref s3, ref counter,
                    ((ulong)(uint)sem.Pairs[1, 1] << 32) | (uint)sem.Pairs[1, 0],
                    ((ulong)(uint)sem.Pairs[2, 1] << 32) | (uint)sem.Pairs[2, 0],
                    ((ulong)(uint)sem.Pairs[3, 1] << 32) | (uint)sem.Pairs[3, 0]);

                at += 80;
            }

            foreach (ChartEvent e in chart.Events)
            {
                Span<byte> record = data.AsSpan(at, 32);
                ulong value1;
                ulong value2;
                if (e.Type == 3)
                {
                    value1 = (uint)((e.Enabled << 8) | e.Track);
                    value2 = 0;
                }
                else
                {
                    value1 = (ulong)BitConverter.DoubleToInt64Bits(e.Value);
                    value2 = e.Meter == 0.0 ? 0UL : (uint)BitConverter.SingleToInt32Bits((float)e.Meter);
                }

                BinaryPrimitives.WriteUInt64LittleEndian(record, (ulong)e.Id);
                BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(8), (uint)e.Time);
                BinaryPrimitives.WriteUInt32LittleEndian(record.Slice(12), (uint)e.Type);
                BinaryPrimitives.WriteUInt64LittleEndian(record.Slice(16), value1);
                BinaryPrimitives.WriteUInt64LittleEndian(record.Slice(24), value2);
                at += 32;
            }

            return data;
        }

        private static NoteSemantics Semantics(ChartNote note)
        {
            int[] p = note.Values;
            int time;
            int duration;
            int x;
            int y;
            int width;
            int x2;
            int y2;
            int width2;
            int flags;
            int track;
            int type;

            switch (note.Kind)
            {
                case "tap":
                    if (p.Length < 3) throw new FormatException("tap needs three values");
                    time = p[0];
                    width = p[1];
                    x = p[2];
                    duration = 0;
                    y = 4;
                    x2 = x;
                    y2 = 4;
                    width2 = width;
                    flags = 0;
                    track = TrackOf(x);
                    type = 1;
                    break;
                case "hold":
                    if (p.Length < 4) throw new FormatException("hold needs four values");
                    time = p[0];
                    x = p[1];
                    width = p[2];
                    duration = p[3];
                    y = 4;
                    x2 = x;
                    y2 = 4;
                    width2 = width;
                    flags = 0;
                    track = TrackOf(x);
                    type = 2;
                    break;
                case "flick":
                    if (p.Length < 5) throw new FormatException("flick needs five values");
                    time = p[0];
                    x = p[1];
                    y = p[2];
                    width = p[3];
                    duration = 0;
                    x2 = x;
                    y2 = y;
                    width2 = width;
                    flags = p[4] << 8;
                    track = 4;
                    type = 4;
                    break;
                case "skyarea":
                    if (p.Length < 11) throw new FormatException("skyarea needs eleven values");
                    time = p[0];
                    x = p[1];
                    y = p[2];
                    width = p[3];
                    x2 = p[4];
                    y2 = p[5];
                    width2 = p[6];
                    duration = p[9];
                    flags = 4 * (1 << p[7]) + 32 * (1 << p[8]);
                    track = 4;
                    type = 5;
                    break;
                default:
                    throw new FormatException("unknown note type: " + note.Kind);
            }

            var pairs = new int[4, 2]
            {
                { x, y },
                { x2, y2 },
                { width, y },
                { width2, y2 },
            };

            var fields = new ulong[10];
            fields[0] = (ulong)note.Index;
            fields[1] = (ulong)note.Line;
            fields[2] = (uint)time;
            fields[3] = (uint)(time + duration);
            fields[4] = (uint)((type << 8) | track);
            fields[5] = (uint)flags;

            var plain = new uint[8];
            for (int i = 0; i < 4; i++)
            {
                int a = pairs[i, 0];
                int b = pairs[i, 1];
                int g = Gcd(a, b);
                if (g == 0) g = 1;
                fields[6 + i] = (uint)g;
                plain[i * 2] = (uint)(a / g);
                plain[i * 2 + 1] = (uint)(b / g);
            }

            return new NoteSemantics
            {
                Fields = fields,
                Pairs = pairs,
                Plain = plain,
            };
        }

        private static int TrackOf(int x) => x >= 1 && x <= 4 ? 1 : (x <= 0 ? 2 : 3);

        private static int Gcd(int a, int b)
        {
            uint x = (uint)Math.Abs((long)a);
            uint y = (uint)Math.Abs((long)b);
            while (y != 0)
            {
                uint t = x % y;
                x = y;
                y = t;
            }
            return (int)x;
        }

        private static int ParseInt(string[] parts, int index, int line)
        {
            if (index >= parts.Length)
                throw new FormatException($"chart line {line + 1}: missing value");
            if (!int.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                throw new FormatException($"chart line {line + 1}: invalid integer {parts[index]}");
            return value;
        }

        private static double ParseDouble(string[] parts, int index, int line)
        {
            if (index >= parts.Length)
                throw new FormatException($"chart line {line + 1}: missing value");
            if (!double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                throw new FormatException($"chart line {line + 1}: invalid number {parts[index]}");
            return value;
        }

        private sealed class Chart
        {
            internal double Bpm;
            internal double Division;
            internal readonly List<ChartEvent> Events = new List<ChartEvent>();
            internal readonly List<ChartNote> Notes = new List<ChartNote>();
        }

        private sealed class RawNote
        {
            internal string Kind;
            internal int[] Values;
        }

        private sealed class ChartNote
        {
            internal string Kind;
            internal int[] Values;
            internal int Index;
            internal int Line;
        }

        private sealed class ChartEvent
        {
            internal int Time;
            internal int Track;
            internal int Enabled;
            internal int Type;
            internal int Id;
            internal double Value;
            internal double Meter;
        }

        private sealed class NoteSemantics
        {
            internal ulong[] Fields;
            internal int[,] Pairs;
            internal uint[] Plain;
        }
    }
}
