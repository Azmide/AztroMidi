using System.IO;
using System.Text;

namespace AztroMidi.Core;

/// <summary>A single note with absolute timing in seconds.</summary>
public sealed class MidiNote
{
    public int Track { get; init; }
    public int Channel { get; init; }
    public int Pitch { get; init; }
    public int Velocity { get; init; }
    public double Start { get; init; }
    public double End { get; set; }
    public double Duration => End - Start;
}

/// <summary>Sustain pedal (CC64) change with absolute timing in seconds.</summary>
public readonly record struct SustainEvent(int Track, int Channel, double Time, bool Down);

public sealed class MidiTrackInfo
{
    public int Index { get; init; }
    public string Name { get; set; } = "";
    public string Instrument { get; set; } = "";
    public HashSet<int> Channels { get; } = new();
    public int NoteCount { get; set; }
    public bool IsDrums => Channels.Count > 0 && Channels.All(c => c == 9);

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(Name) ? Name.Trim()
        : !string.IsNullOrWhiteSpace(Instrument) ? Instrument.Trim()
        : $"Track {Index + 1}";
}

/// <summary>
/// Minimal Standard MIDI File (format 0/1) reader. Converts ticks to seconds
/// using the full tempo map so playback stays in sync on tempo changes.
/// </summary>
public sealed class MidiFile
{
    public string Path { get; private set; } = "";
    public string Title => System.IO.Path.GetFileNameWithoutExtension(Path);
    public List<MidiNote> Notes { get; } = new();
    public List<SustainEvent> Sustain { get; } = new();
    public List<MidiTrackInfo> Tracks { get; } = new();
    public double Length { get; private set; }
    public double InitialBpm { get; private set; } = 120;

    private readonly record struct RawEvent(long Tick, int Track, int Order, byte Status, byte D1, byte D2, int Tempo);

    public static MidiFile Load(string path)
    {
        var data = File.ReadAllBytes(path);
        var midi = new MidiFile { Path = path };
        midi.Parse(data);
        return midi;
    }

    private void Parse(byte[] data)
    {
        int pos = 0;
        // Some files carry a RIFF (RMID) wrapper; jump to the MThd chunk.
        int mthd = IndexOf(data, "MThd"u8);
        if (mthd < 0) throw new InvalidDataException("Not a MIDI file (MThd header missing).");
        pos = mthd + 4;

        int headerLen = ReadInt32(data, ref pos);
        int headerStart = pos;
        int format = ReadInt16(data, ref pos);
        int trackCount = ReadInt16(data, ref pos);
        int division = ReadInt16(data, ref pos);
        pos = headerStart + headerLen;

        if (format > 2) throw new InvalidDataException($"Unsupported MIDI format {format}.");
        bool smpte = (division & 0x8000) != 0;
        double ticksPerQuarter = division & 0x7FFF;
        double smpteTickSeconds = 0;
        if (smpte)
        {
            int fps = -(sbyte)(division >> 8);
            int ticksPerFrame = division & 0xFF;
            smpteTickSeconds = 1.0 / (fps * ticksPerFrame);
        }
        if (!smpte && ticksPerQuarter <= 0) ticksPerQuarter = 480;

        var events = new List<RawEvent>(8192);
        int order = 0;

        for (int t = 0; t < trackCount && pos + 8 <= data.Length; t++)
        {
            // Skip unknown chunks until the next MTrk.
            while (pos + 8 <= data.Length && !Matches(data, pos, "MTrk"u8))
            {
                pos += 4;
                int skip = ReadInt32(data, ref pos);
                pos += skip;
            }
            if (pos + 8 > data.Length) break;
            pos += 4;
            int len = ReadInt32(data, ref pos);
            int end = Math.Min(data.Length, pos + len);

            var info = new MidiTrackInfo { Index = t };
            Tracks.Add(info);

            long tick = 0;
            byte running = 0;
            while (pos < end)
            {
                tick += ReadVarLen(data, ref pos);
                if (pos >= end) break;
                byte status = data[pos];
                if (status < 0x80)
                {
                    if (running == 0) { pos++; continue; } // corrupt data, resync
                    status = running;
                }
                else
                {
                    pos++;
                    if (status < 0xF0) running = status;
                }

                if (status == 0xFF)
                {
                    byte type = data[pos++];
                    int mlen = (int)ReadVarLen(data, ref pos);
                    if (pos + mlen > data.Length) mlen = Math.Max(0, data.Length - pos);
                    switch (type)
                    {
                        case 0x03 when info.Name.Length == 0:
                            info.Name = ReadText(data, pos, mlen);
                            break;
                        case 0x04 when info.Instrument.Length == 0:
                            info.Instrument = ReadText(data, pos, mlen);
                            break;
                        case 0x51 when mlen >= 3:
                            int tempo = (data[pos] << 16) | (data[pos + 1] << 8) | data[pos + 2];
                            events.Add(new RawEvent(tick, t, order++, 0xFF, 0x51, 0, tempo));
                            break;
                    }
                    pos += mlen;
                    if (type == 0x2F) break;
                    continue;
                }
                if (status == 0xF0 || status == 0xF7)
                {
                    int slen = (int)ReadVarLen(data, ref pos);
                    pos += slen;
                    continue;
                }

                int kind = status & 0xF0;
                byte d1 = data[pos++];
                byte d2 = 0;
                if (kind != 0xC0 && kind != 0xD0) d2 = pos < data.Length ? data[pos++] : (byte)0;

                if (kind is 0x80 or 0x90 || (kind == 0xB0 && d1 == 64))
                {
                    events.Add(new RawEvent(tick, t, order++, status, d1, d2, 0));
                    info.Channels.Add(status & 0x0F);
                }
                else if (kind == 0xC0)
                {
                    info.Channels.Add(status & 0x0F);
                    if (info.Instrument.Length == 0 && (status & 0x0F) != 9)
                        info.Instrument = GeneralMidi.InstrumentName(d1);
                }
            }
            pos = end;
        }

        events.Sort((a, b) =>
        {
            int c = a.Tick.CompareTo(b.Tick);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        });

        // Walk the merged stream once, converting ticks to seconds.
        double usPerQuarter = 500000;
        bool firstTempo = true;
        long lastTick = 0;
        double seconds = 0;
        var open = new Dictionary<int, Stack<MidiNote>>();

        foreach (var e in events)
        {
            long delta = e.Tick - lastTick;
            seconds += smpte ? delta * smpteTickSeconds : delta * (usPerQuarter / 1_000_000.0) / ticksPerQuarter;
            lastTick = e.Tick;

            if (e.Status == 0xFF)
            {
                if (e.Tempo > 0) usPerQuarter = e.Tempo;
                if (firstTempo && e.Tick == 0) InitialBpm = Math.Round(60_000_000.0 / usPerQuarter, 1);
                firstTempo = false;
                continue;
            }

            int kind = e.Status & 0xF0;
            int ch = e.Status & 0x0F;
            if (kind == 0xB0)
            {
                Sustain.Add(new SustainEvent(e.Track, ch, seconds, e.D2 >= 64));
                continue;
            }

            int key = (e.Track << 16) | (ch << 8) | e.D1;
            if (kind == 0x90 && e.D2 > 0)
            {
                var n = new MidiNote { Track = e.Track, Channel = ch, Pitch = e.D1, Velocity = e.D2, Start = seconds, End = seconds };
                Notes.Add(n);
                if (!open.TryGetValue(key, out var stack)) open[key] = stack = new Stack<MidiNote>();
                stack.Push(n);
                Tracks[e.Track].NoteCount++;
            }
            else if (open.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                stack.Pop().End = seconds;
            }
        }

        foreach (var stack in open.Values)
            foreach (var n in stack)
                n.End = Math.Max(n.Start + 0.25, seconds);

        foreach (var n in Notes)
            if (n.End <= n.Start) n.End = n.Start + 0.05;

        Notes.Sort((a, b) => a.Start.CompareTo(b.Start));
        Length = Notes.Count == 0 ? 0 : Notes.Max(n => n.End);
    }

    private static string ReadText(byte[] d, int pos, int len)
    {
        var s = Encoding.Latin1.GetString(d, pos, len);
        return new string(s.Where(c => !char.IsControl(c)).ToArray()).Trim();
    }

    private static int IndexOf(byte[] d, ReadOnlySpan<byte> tag) => d.AsSpan().IndexOf(tag);
    private static bool Matches(byte[] d, int pos, ReadOnlySpan<byte> tag) => d.AsSpan(pos, 4).SequenceEqual(tag);

    private static int ReadInt32(byte[] d, ref int pos)
    {
        int v = (d[pos] << 24) | (d[pos + 1] << 16) | (d[pos + 2] << 8) | d[pos + 3];
        pos += 4;
        return v;
    }

    private static int ReadInt16(byte[] d, ref int pos)
    {
        int v = (d[pos] << 8) | d[pos + 1];
        pos += 2;
        return v;
    }

    private static long ReadVarLen(byte[] d, ref int pos)
    {
        long v = 0;
        for (int i = 0; i < 4 && pos < d.Length; i++)
        {
            byte b = d[pos++];
            v = (v << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) break;
        }
        return v;
    }
}
