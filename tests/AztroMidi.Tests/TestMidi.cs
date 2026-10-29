using System.IO;

namespace AztroMidi.Tests;

/// <summary>Builds small Standard MIDI Files in memory for the tests.</summary>
internal static class TestMidi
{
    private static byte[] VarLen(long n)
    {
        var bytes = new List<byte> { (byte)(n & 0x7F) };
        n >>= 7;
        while (n > 0)
        {
            bytes.Insert(0, (byte)((n & 0x7F) | 0x80));
            n >>= 7;
        }
        return bytes.ToArray();
    }

    private static byte[] Track(params (int Delta, byte[] Data)[] events)
    {
        var body = new List<byte>();
        foreach (var (delta, data) in events)
        {
            body.AddRange(VarLen(delta));
            body.AddRange(data);
        }
        body.AddRange(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });
        var chunk = new List<byte>("MTrk"u8.ToArray());
        chunk.AddRange(BigEndian(body.Count, 4));
        chunk.AddRange(body);
        return chunk.ToArray();
    }

    private static byte[] BigEndian(int value, int size)
    {
        var b = new byte[size];
        for (int i = size - 1; i >= 0; i--, value >>= 8) b[i] = (byte)(value & 0xFF);
        return b;
    }

    private static byte[] Text(byte type, string s) =>
        new byte[] { 0xFF, type, (byte)s.Length }.Concat(System.Text.Encoding.ASCII.GetBytes(s)).ToArray();

    private static byte[] Tempo(int usPerQuarter) => new byte[] { 0xFF, 0x51, 0x03 }.Concat(BigEndian(usPerQuarter, 3)).ToArray();

    /// <summary>
    /// Format 1, 480 ticks per quarter, three tracks:
    ///  0: tempo 120 bpm, then 60 bpm at tick 960
    ///  1: "Piano": C major chord (running status), C5 at 1.0 s, pedal, C1 at 2.0 s
    ///  2: one drum hit on channel 10
    /// </summary>
    public static string WriteSample()
    {
        var t0 = Track(
            (0, Text(0x03, "Tempo")),
            (0, Tempo(500_000)),
            (960, Tempo(1_000_000)));
        var t1 = Track(
            (0, Text(0x03, "Piano")),
            (0, new byte[] { 0x90, 0x3C, 0x64 }), (0, new byte[] { 0x40, 0x64 }), (0, new byte[] { 0x43, 0x64 }),
            (480, new byte[] { 0x3C, 0x00 }), (0, new byte[] { 0x40, 0x00 }), (0, new byte[] { 0x43, 0x00 }),
            (480, new byte[] { 0x90, 0x48, 0x50 }),
            (480, new byte[] { 0x80, 0x48, 0x00 }),
            (0, new byte[] { 0xB0, 0x40, 0x7F }),
            (0, new byte[] { 0x90, 0x18, 0x40 }),
            (480, new byte[] { 0x80, 0x18, 0x00 }),
            (0, new byte[] { 0xB0, 0x40, 0x00 }));
        var t2 = Track(
            (0, new byte[] { 0x99, 0x24, 0x64 }),
            (240, new byte[] { 0x89, 0x24, 0x00 }));

        var file = new List<byte>("MThd"u8.ToArray());
        file.AddRange(BigEndian(6, 4));
        file.AddRange(BigEndian(1, 2));
        file.AddRange(BigEndian(3, 2));
        file.AddRange(BigEndian(480, 2));
        file.AddRange(t0);
        file.AddRange(t1);
        file.AddRange(t2);

        var path = Path.Combine(Path.GetTempPath(), $"aztromidi-test-{Environment.ProcessId}.mid");
        File.WriteAllBytes(path, file.ToArray());
        return path;
    }
}
